using OpenTK.Graphics.OpenGL4;
using System;
using System.IO;
using System.Reflection;

namespace GlideTK;

/// <summary>
/// Compiles and links shaders embedded into the assembly
/// </summary>
public static class ShaderLoader
{
    /// <summary>
    /// A program that is compiling and linking, perhaps in the driver threads.
    /// A compute program has no vertex shader (0), its shader is the Fragment one.
    /// </summary>
    public readonly record struct PendingProgram(
        int Program,
        int Vertex,
        int Fragment,
        string VertexFilename,
        string FragmentFilename);

    // GL_COMPLETION_STATUS_KHR of GL_KHR_parallel_shader_compile
    const int COMPLETION_STATUS = 0x91B1;

    public static string LoadEmbeddedFile(string filename)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(filename);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// Starts compiling, the status is not queried: that would wait for the driver
    /// </summary>
    /// <param name="patch">Changes the source before compiling,
    /// e.g. injects the generated code</param>
    static int StartShader(
        ShaderType type,
        string filename,
        Func<string, string> patch = null)
    {
        string source = LoadEmbeddedFile(filename);
        if (patch != null)
            source = patch(source);

        int h_shader = GL.CreateShader(type);
        GL.ShaderSource(h_shader, source);
        GL.CompileShader(h_shader);

        return h_shader;
    }

    /// <returns>Compile error or null</returns>
    static string CompileError(int h_shader, string filename)
    {
        GL.GetShader(h_shader, ShaderParameter.CompileStatus, out int status);

        return status == 0
            ? $"{filename} compile error:\n{GL.GetShaderInfoLog(h_shader)}"
            : null;
    }

    /// <summary>
    /// Starts compiling and linking a program of a vertex and a fragment shader,
    /// does not wait for the driver: poll IsDone and call FinishLink
    /// </summary>
    /// <param name="patchFragment">Changes the fragment source before compiling</param>
    public static PendingProgram BeginLink(
        string vertexFilename,
        string fragmentFilename,
        Func<string, string> patchFragment = null)
    {
        int h_vertex = StartShader(ShaderType.VertexShader, vertexFilename);
        int h_fragment = StartShader(ShaderType.FragmentShader, fragmentFilename, patchFragment);

        int h_program = GL.CreateProgram();

        GL.AttachShader(h_program, h_vertex);
        GL.AttachShader(h_program, h_fragment);

        GL.LinkProgram(h_program);

        return new PendingProgram(
            h_program,
            h_vertex,
            h_fragment,
            vertexFilename,
            fragmentFilename);
    }

    /// <summary>
    /// Starts compiling and linking a program of one compute shader,
    /// like <see cref="BeginLink"/>
    /// </summary>
    public static PendingProgram BeginComputeLink(
        string filename,
        Func<string, string> patch = null)
    {
        int h_shader = StartShader(ShaderType.ComputeShader, filename, patch);

        int h_program = GL.CreateProgram();
        GL.AttachShader(h_program, h_shader);
        GL.LinkProgram(h_program);

        return new PendingProgram(h_program, 0, h_shader, null, filename);
    }

    /// <summary>
    /// The driver has finished compiling and linking (needs GL_KHR_parallel_shader_compile)
    /// </summary>
    public static bool IsDone(PendingProgram pending)
    {
        GL.GetProgram(
            pending.Program,
            (GetProgramParameterName)COMPLETION_STATUS,
            out int done);

        return done != 0;
    }

    /// <summary>
    /// Waits for the program if it is not done yet,
    /// the shaders are deleted after linking
    /// </summary>
    /// <returns>The linked program</returns>
    /// <exception cref="InvalidOperationException">Compile or link error</exception>
    public static int FinishLink(PendingProgram pending)
    {
        string error = (pending.Vertex != 0 
            ? CompileError(pending.Vertex, pending.VertexFilename) 
            : null)
            ?? CompileError(pending.Fragment, pending.FragmentFilename);

        if (error == null)
        {
            GL.GetProgram(pending.Program, GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
            {
                error = $"Shader program ({pending.FragmentFilename}) link error:\n" +
                    GL.GetProgramInfoLog(pending.Program);
            }
        }

        foreach (int shader in new[] { pending.Vertex, pending.Fragment })
        {
            if (shader == 0)
                continue;

            GL.DetachShader(pending.Program, shader);
            GL.DeleteShader(shader);
        }

        if (error != null)
        {
            GL.DeleteProgram(pending.Program);

            throw new InvalidOperationException(error);
        }

        return pending.Program;
    }

    /// <summary>
    /// Links a program of a vertex and a fragment shader and waits for it
    /// </summary>
    /// <param name="patchFragment">Changes the fragment source before compiling</param>
    public static int LinkProgram(
        string vertexFilename,
        string fragmentFilename,
        Func<string, string> patchFragment = null)
        => FinishLink(BeginLink(vertexFilename, fragmentFilename, patchFragment));

    /// <summary>
    /// Links a program of one compute shader and waits for it
    /// </summary>
    public static int LinkCompute(
        string filename,
        Func<string, string> patch = null)
        => FinishLink(BeginComputeLink(filename, patch));
}
