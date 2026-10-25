using GlideTK.Scene;
using GlideTK.Scene.Glsl;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Diagnostics;


namespace GlideTK;

public class Render
{
    int h_shaderProgram,    // trace.c, built for the scene
        h_cullProgram;      // cull.c

    int ubo_GlobalMap,
        ssbo_TileLists,
        tex_Output,
        fbo_Output;         // the read side of the blit to the window

    // the size the output picture and the tile lists were made for
    int targetWidth, targetHeight;

    /// <summary>
    /// Skip scene objects by their bounding boxes, off evaluates every object (to compare the speed)
    /// </summary>
    public bool Bvh { get; set; } = true;

    /// <summary>
    /// Trace through the lists of leaves that matter for every tile (cull.c),
    /// off walks the whole BVH for every step (to compare the speed)
    /// </summary>
    public bool Tiles { get; set; } = true;

    /// <summary>
    /// The last frame was traced with the tile lists: <see cref="Tiles"/> is on
    /// and the scene has enough objects for them to pay off
    /// </summary>
    public bool TilesUsed { get; private set; }

    int mapBlockSize;
    int uploadedSceneVersion = -1;
    int uploadedNodeCount;

    // the scene revision the running shader was built for,
    // the scene data is uploaded only when it matches the shader
    int installedRevision = -1;
    string builtSceneGlsl;

    // GL_KHR_parallel_shader_compile: the shader is built in the driver threads
    // while the running one keeps drawing the previous picture
    bool asyncCompile;
    ShaderLoader.PendingProgram? pending;
    int pendingRevision;
    string pendingGlsl;

    // a blocking build freezes the window: the caption frames are shown for a while first,
    // otherwise the driver has not presented them yet when the build blocks
    const double ANNOUNCE_SECONDS = 0.2;
    bool announced;
    readonly Stopwatch announceWatch = new();
    readonly Stopwatch compileWatch = new();

    /// <summary>
    /// The shader is building (or about to), the picture is still of the previous scene
    /// or black at the first start
    /// </summary>
    public bool Compiling => pending != null || announced;

    /// <summary>The caption frames before a blocking build, the window freezes next</summary>
    public bool Announced => announced;

    /// <summary>0 until the build has started, the blocking build never shows more</summary>
    public double CompilingSeconds => pending != null ? compileWatch.Elapsed.TotalSeconds : 0;

    /// <summary>
    /// Error of the last shader generated for the scene, null when it built fine.
    /// On an error the previous shader keeps running.
    /// </summary>
    public string ShaderError { get; private set; }

    private static bool HasExtension(string name)
    {
        GL.GetInteger(GetPName.NumExtensions, out int count);
        for (int i = 0; i < count; i++)
        {
            if (GL.GetString(StringNameIndexed.Extensions, i) == name)
                return true;
        }

        return false;
    }

    private bool NeedsBuild(SceneState scene)
    {
        return SceneGlsl.Generate(scene.Shapes) != builtSceneGlsl;
    }

    /// <summary>
    /// Generates the scene functions of the trace shader and starts building the program,
    /// nothing changes when the generated code is the same as in the running shader.
    /// Without the parallel compile the program is installed right here.
    /// </summary>
    private void StartBuild(SceneState scene)
    {
        string sceneGlsl = SceneGlsl.Generate(scene.Shapes);
        if (sceneGlsl == builtSceneGlsl)
        {
            installedRevision = scene.ShaderRevision;
            return;
        }

        pending = ShaderLoader.BeginComputeLink(
            Const.TRACE_FILENAME,
            source => SceneGlsl.Inject(source, scene.Shapes));
        pendingRevision = scene.ShaderRevision;
        pendingGlsl = sceneGlsl;
        compileWatch.Restart();

        if (!asyncCompile)
            FinishBuild();
    }

    /// <summary>
    /// Replaces the running shader with the built one, on an error the previous one keeps running
    /// </summary>
    private void FinishBuild()
    {
        var built = pending.Value;
        pending = null;
        installedRevision = pendingRevision;

        int program;
        try
        {
            program = ShaderLoader.FinishLink(built);
        }
        catch (InvalidOperationException e)
        {
            ShaderError = e.Message;
            return;
        }

        if (h_shaderProgram != 0)
            GL.DeleteProgram(h_shaderProgram);

        h_shaderProgram = program;
        builtSceneGlsl = pendingGlsl;
        ShaderError = null;

        if (ubo_GlobalMap == 0)
            CreateMapUbo();
    }

    public void Start(SceneState scene)
    {
        asyncCompile = HasExtension("GL_KHR_parallel_shader_compile");
        if (asyncCompile)
            GL.Khr.MaxShaderCompilerThreads(uint.MaxValue);

        h_cullProgram = ShaderLoader.LinkCompute(Const.CULL_FILENAME);

        GL.Disable(EnableCap.DepthTest);
    }

    private void CreateMapUbo()
    {
        // std140: an array of vec4 has no padding, the block is exactly BLOCKCOUNT vec4
        mapBlockSize = Const.UBO_SDELEMENTSMAP_BLOCKCOUNT * 16;

        ubo_GlobalMap = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.UniformBuffer, ubo_GlobalMap);

        GL.BufferData(
            BufferTarget.UniformBuffer,
            mapBlockSize,
            IntPtr.Zero,
            BufferUsageHint.StreamDraw);

        GL.BindBufferBase(
            BufferRangeTarget.UniformBuffer,
            Const.UBO_SDELEMENTSMAP_BINDING,
            ubo_GlobalMap);
        GL.BindBuffer(BufferTarget.UniformBuffer, 0);
    }

    /// <summary>
    /// Uploads the compiled scene program when it changed since the last frame
    /// </summary>
    private void UploadScene(SceneState scene)
    {
        if (scene.Version == uploadedSceneVersion)
            return;

        const int VEC4_SIZE = 16;
        int size = scene.Data.Length * VEC4_SIZE;
        if (size > mapBlockSize)
        {
            throw new InvalidOperationException(
                $"Scene program ({size} bytes) " +
                    $"exceeds {Const.UBO_SDELEMENTSMAP_BLOCKNAME} " +
                    $"block ({mapBlockSize} bytes)");
        }

        GL.BindBuffer(BufferTarget.UniformBuffer, ubo_GlobalMap);
        GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, size, scene.Data);
        GL.BindBuffer(BufferTarget.UniformBuffer, 0);

        uploadedSceneVersion = scene.Version;
        uploadedNodeCount = (int)scene.Data[0].Z;
    }

    static int TilesAcross(int pixels) =>
        (pixels + Const.TILE_SIZE - 1) / Const.TILE_SIZE;

    /// <summary>
    /// The output picture and the tile lists follow the size of the window
    /// </summary>
    private void EnsureTarget(int width, int height)
    {
        if (width == targetWidth 
            && height == targetHeight)
        {
            return;
        }

        if (tex_Output != 0)
        {
            GL.DeleteTexture(tex_Output);
            GL.DeleteFramebuffer(fbo_Output);
            GL.DeleteBuffer(ssbo_TileLists);
        }

        tex_Output = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex_Output);
        GL.TexStorage2D(
            TextureTarget2d.Texture2D, 
            1, 
            SizedInternalFormat.Rgba8, 
            width, 
            height);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        fbo_Output = GL.GenFramebuffer();
        GL.GetInteger(GetPName.ReadFramebufferBinding, out int readBinding);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo_Output);
        GL.FramebufferTexture2D(
            FramebufferTarget.ReadFramebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            tex_Output,
            0);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readBinding);

        int tiles = TilesAcross(width) * TilesAcross(height);
        ssbo_TileLists = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, ssbo_TileLists);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            tiles * Const.TILE_STRIDE * sizeof(int),
            IntPtr.Zero,
            BufferUsageHint.DynamicCopy);
        GL.BindBufferBase(
            BufferRangeTarget.ShaderStorageBuffer,
            Const.TILE_LISTS_BINDING,
            ssbo_TileLists);
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);

        targetWidth = width;
        targetHeight = height;
    }

    internal void OnResize(int width, int height)
    {
        GL.Viewport(0, 0, width, height);
    }

    internal void OnFrame(
        int width, 
        int height, 
        Camera camera, 
        SceneState scene)
    {
        if (pending != null
            && (!asyncCompile 
                || ShaderLoader.IsDone(pending.Value)))
        {
            FinishBuild();
        }

        if (pending == null 
            && scene.ShaderRevision != installedRevision)
        {
            // a blocking build freezes the window: draw the caption frame first
            if (!announced 
                && !asyncCompile 
                && NeedsBuild(scene))
            {
                announced = true;
                announceWatch.Restart();
            }
            else if (!announced 
                || announceWatch.Elapsed.TotalSeconds >= ANNOUNCE_SECONDS)
            {
                announced = false;
                StartBuild(scene);
            }
        }

        // nothing to draw until the first shader is built
        if (h_shaderProgram == 0 
            || width <= 0 
            || height <= 0)
        {
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            return;
        }

        EnsureTarget(width, height);

        // while the new shader builds, the old one keeps the old scene data
        if (scene.ShaderRevision == installedRevision)
            UploadScene(scene);

        int tilesX = TilesAcross(width);
        int tilesY = TilesAcross(height);

        // the leaves that matter for every tile
        TilesUsed = Tiles && uploadedNodeCount >= Const.TILE_MIN_NODES;
        if (TilesUsed)
        {
            GL.UseProgram(h_cullProgram);
            GL.Uniform3(Const.UF_LOC_RESOLUTION, width, height, 0.0f);
            GL.Uniform3(Const.UF_LOC_RAY_ORIGIN, camera.Origin);
            GL.UniformMatrix3(Const.UF_LOC_PROJECTION_MATRIX, false, ref camera.Projection);
            GL.Uniform3(Const.UF_LOC_SUN, scene.Env.Sun);
            GL.Uniform2(Const.UF_LOC_SHADOWS, scene.Env.Shadows);
            GL.Uniform3(Const.UF_LOC_MARCH, scene.Env.March);
            GL.DispatchCompute(tilesX, tilesY, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        }

        // the picture
        GL.UseProgram(h_shaderProgram);
        GL.Uniform3(Const.UF_LOC_RESOLUTION, width, height, 0.0f);
        GL.Uniform3(Const.UF_LOC_RAY_ORIGIN, camera.Origin);
        GL.UniformMatrix3(Const.UF_LOC_PROJECTION_MATRIX, false, ref camera.Projection);
        GL.Uniform1(Const.UF_LOC_USE_BVH, Bvh ? 1 : 0);
        GL.Uniform3(Const.UF_LOC_SUN, scene.Env.Sun);
        GL.Uniform3(Const.UF_LOC_FOG_COLOR, scene.Env.FogColor);
        GL.Uniform1(Const.UF_LOC_FOG_DENSITY, scene.Env.FogDensity);
        GL.Uniform3(Const.UF_LOC_SKY, scene.Env.Sky);
        GL.Uniform4(Const.UF_LOC_LIGHT, scene.Env.Light);
        GL.Uniform2(Const.UF_LOC_SHADOWS, scene.Env.Shadows);
        GL.Uniform3(Const.UF_LOC_MARCH, scene.Env.March);
        GL.Uniform1(Const.UF_LOC_BOUNCES, scene.Env.Bounces);
        GL.Uniform1(Const.UF_LOC_TILES, TilesUsed ? 1 : 0);
        GL.BindImageTexture(
            Const.OUTPUT_IMAGE_BINDING,
            tex_Output,
            0,
            false,
            0,
            TextureAccess.WriteOnly,
            SizedInternalFormat.Rgba8);
        GL.DispatchCompute(tilesX, tilesY, 1);
        GL.MemoryBarrier(
            MemoryBarrierFlags.FramebufferBarrierBit 
            | MemoryBarrierFlags.TextureFetchBarrierBit);

        // to the framebuffer being drawn: the window, or the one of a test
        GL.GetInteger(GetPName.ReadFramebufferBinding, out int readBinding);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo_Output);
        GL.BlitFramebuffer(
            0, 0, width, height,
            0, 0, width, height,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readBinding);

        GL.UseProgram(0);
    }

    internal void Stop()
    {
        GL.DeleteTexture(tex_Output);
        GL.DeleteFramebuffer(fbo_Output);
        GL.DeleteBuffer(ssbo_TileLists);
        GL.DeleteProgram(h_cullProgram);
        GL.DeleteBuffer(ubo_GlobalMap);
        GL.DeleteProgram(h_shaderProgram);

        if (pending != null)
        {
            GL.DeleteProgram(pending.Value.Program);
        }
    }
}
