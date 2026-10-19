using GlideTK.Scene;
using GlideTK.Scene.Glsl;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Diagnostics;


namespace GlideTK;

public class Render
{
    int h_shaderProgram,
        h_vao;

    int uf_iGlobalTime,
        uf_iResolution,
        uf_CamRo,
        um3_CamProj,
        uf_UseBvh,
        uf_Sun,
        uf_FogColor,
        uf_FogDensity,
        uf_Sky,
        uf_Light,
        uf_Shadows,
        uf_March,
        uf_Bounces,
        ubo_GlobalMap;

    /// <summary>
    /// Skip scene objects by their bounding boxes, off evaluates every object (to compare the speed)
    /// </summary>
    public bool Bvh { get; set; } = true;

    int mapBlockSize;
    int uploadedSceneVersion = -1;

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

    private int GetUniformLocation(string uniformName)
    {
        return GL.GetUniformLocation(h_shaderProgram, uniformName);
    }

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
    /// Generates the scene functions of the fragment shader and starts building the program,
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

        pending = ShaderLoader.BeginLink(
            Const.VERTEX_FILENAME,
            Const.FRAGMENT_FILENAME,
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

        GL.UseProgram(h_shaderProgram);
        BindMapBlock();
        if (ubo_GlobalMap == 0)
            CreateMapUbo();

        uf_iGlobalTime = GetUniformLocation(Const.UF_TIMER);
        uf_iResolution = GetUniformLocation(Const.UF_RESOLUTION);
        uf_CamRo = GetUniformLocation(Const.UF_RAY_ORIGIN);
        um3_CamProj = GetUniformLocation(Const.UF_PROJECTION_MATRIX);
        uf_UseBvh = GetUniformLocation(Const.UF_USE_BVH);
        uf_Sun = GetUniformLocation(Const.UF_SUN);
        uf_FogColor = GetUniformLocation(Const.UF_FOG_COLOR);
        uf_FogDensity = GetUniformLocation(Const.UF_FOG_DENSITY);
        uf_Sky = GetUniformLocation(Const.UF_SKY);
        uf_Light = GetUniformLocation(Const.UF_LIGHT);
        uf_Shadows = GetUniformLocation(Const.UF_SHADOWS);
        uf_March = GetUniformLocation(Const.UF_MARCH);
        uf_Bounces = GetUniformLocation(Const.UF_BOUNCES);
    }

    public void Start(SceneState scene)
    {
        asyncCompile = HasExtension("GL_KHR_parallel_shader_compile");
        if (asyncCompile)
            GL.Khr.MaxShaderCompilerThreads(uint.MaxValue);

        // core profile requires a bound VAO,
        // the fullscreen triangle is built from gl_VertexID
        h_vao = GL.GenVertexArray();
        GL.BindVertexArray(h_vao);

        GL.Disable(EnableCap.DepthTest);
    }

    private void BindMapBlock()
    {
        int block_index = GL.GetUniformBlockIndex(
            h_shaderProgram, 
            Const.UBO_SDELEMENTSMAP_BLOCKNAME);
        GL.UniformBlockBinding(
            h_shaderProgram, 
            block_index, 
            Const.UBO_SDELEMENTSMAP_BINDING);
    }

    private void CreateMapUbo()
    {
        int block_index = GL.GetUniformBlockIndex(
            h_shaderProgram, 
            Const.UBO_SDELEMENTSMAP_BLOCKNAME);

        // Allocate space for the buffer
        GL.GetActiveUniformBlock(
            h_shaderProgram,
            block_index,
            ActiveUniformBlockParameter.UniformBlockDataSize,
            out mapBlockSize);

        // Create the buffer object
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
    }

    internal void OnResize(int width, int height)
    {
        GL.Viewport(0, 0, width, height);
    }

    internal void OnFrame(
        float globalTime, 
        int width, 
        int height, 
        Camera camera, 
        SceneState scene)
    {
        if (pending != null && (!asyncCompile || ShaderLoader.IsDone(pending.Value)))
            FinishBuild();
        if (pending == null && scene.ShaderRevision != installedRevision)
        {
            // a blocking build freezes the window: draw the caption frame first
            if (!announced && !asyncCompile && NeedsBuild(scene))
            {
                announced = true;
                announceWatch.Restart();
            }
            else if (!announced || announceWatch.Elapsed.TotalSeconds >= ANNOUNCE_SECONDS)
            {
                announced = false;
                StartBuild(scene);
            }
        }

        // nothing to draw until the first shader is built
        if (h_shaderProgram == 0)
        {
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            return;
        }

        GL.UseProgram(h_shaderProgram);
        GL.BindVertexArray(h_vao);

        GL.Uniform1(uf_iGlobalTime, globalTime);
        GL.Uniform3(uf_iResolution, width, height, 0.0f);
        GL.Uniform3(uf_CamRo, camera.Origin);
        GL.UniformMatrix3(um3_CamProj, false, ref camera.Projection);
        GL.Uniform1(uf_UseBvh, Bvh ? 1 : 0);
        GL.Uniform3(uf_Sun, scene.Env.Sun);
        GL.Uniform3(uf_FogColor, scene.Env.FogColor);
        GL.Uniform1(uf_FogDensity, scene.Env.FogDensity);
        GL.Uniform3(uf_Sky, scene.Env.Sky);
        GL.Uniform4(uf_Light, scene.Env.Light);
        GL.Uniform2(uf_Shadows, scene.Env.Shadows);
        GL.Uniform3(uf_March, scene.Env.March);
        GL.Uniform1(uf_Bounces, scene.Env.Bounces);

        // while the new shader builds, the old one keeps the old scene data
        if (scene.ShaderRevision == installedRevision)
            UploadScene(scene);

        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.UseProgram(0);
    }

    internal void Stop()
    {
        GL.DeleteVertexArray(h_vao);
        GL.DeleteBuffer(ubo_GlobalMap);
        GL.DeleteProgram(h_shaderProgram);
        if (pending != null)
            GL.DeleteProgram(pending.Value.Program);
    }
}
