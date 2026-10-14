using GlideTK.Scene;
using OpenTK.Graphics.OpenGL4;
using System;


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
        ubo_GlobalMap;

    /// <summary>
    /// Skip scene objects by their bounding boxes, off evaluates every object (to compare the speed)
    /// </summary>
    public bool Bvh { get; set; } = true;

    int mapBlockSize;
    int uploadedSceneVersion = -1;

    private int GetUniformLocation(string uniformName)
    {
        return GL.GetUniformLocation(h_shaderProgram, uniformName);
    }

    private void CreateShaders()
    {
        h_shaderProgram = ShaderLoader.LinkProgram(
            Const.VERTEX_FILENAME, 
            Const.FRAGMENT_FILENAME);

        GL.UseProgram(h_shaderProgram);
        CreateMapUbo();

        uf_iGlobalTime = GetUniformLocation(Const.UF_TIMER);
        uf_iResolution = GetUniformLocation(Const.UF_RESOLUTION);
        uf_CamRo = GetUniformLocation(Const.UF_RAY_ORIGIN);
        um3_CamProj = GetUniformLocation(Const.UF_PROJECTION_MATRIX);
        uf_UseBvh = GetUniformLocation(Const.UF_USE_BVH);
    }

    public void Start()
    {
        CreateShaders();

        // core profile requires a bound VAO,
        // the fullscreen triangle is built from gl_VertexID
        h_vao = GL.GenVertexArray();
        GL.BindVertexArray(h_vao);

        GL.Disable(EnableCap.DepthTest);
    }

    private void CreateMapUbo()
    {
        int block_index = GL.GetUniformBlockIndex(
            h_shaderProgram, 
            Const.UBO_SDELEMENTSMAP_BLOCKNAME);
        GL.UniformBlockBinding(
            h_shaderProgram, 
            block_index, 
            Const.UBO_SDELEMENTSMAP_BINDING);

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
        GL.UseProgram(h_shaderProgram);
        GL.BindVertexArray(h_vao);

        GL.Uniform1(uf_iGlobalTime, globalTime);
        GL.Uniform3(uf_iResolution, width, height, 0.0f);
        GL.Uniform3(uf_CamRo, camera.Origin);
        GL.UniformMatrix3(um3_CamProj, false, ref camera.Projection);
        GL.Uniform1(uf_UseBvh, Bvh ? 1 : 0);

        UploadScene(scene);

        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.UseProgram(0);
    }

    internal void Stop()
    {
        GL.DeleteVertexArray(h_vao);
        GL.DeleteBuffer(ubo_GlobalMap);
        GL.DeleteProgram(h_shaderProgram);
    }
}
