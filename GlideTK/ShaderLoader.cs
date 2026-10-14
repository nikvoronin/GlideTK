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
    public static string LoadEmbeddedFile(string filename)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(filename);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <param name="patch">Changes the source before compiling,
    /// e.g. injects the generated code</param>
    public static int CompileShader(
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

        GL.GetShader(h_shader, ShaderParameter.CompileStatus, out int status);
        if (status == 0)
        {
            string log = GL.GetShaderInfoLog(h_shader);
            GL.DeleteShader(h_shader);

            throw new InvalidOperationException(
                $"{filename} compile error:\n{log}");
        }

        return h_shader;
    }

    /// <summary>
    /// Links a program of a vertex and a fragment shader, 
    /// the shaders are deleted after linking
    /// </summary>
    /// <param name="patchFragment">Changes the fragment source before compiling</param>
    public static int LinkProgram(
        string vertexFilename,
        string fragmentFilename,
        Func<string, string> patchFragment = null)
    {
        int h_vertex = CompileShader(ShaderType.VertexShader, vertexFilename);
        int h_fragment = CompileShader(ShaderType.FragmentShader, fragmentFilename, patchFragment);

        int h_program = GL.CreateProgram();

        GL.AttachShader(h_program, h_vertex);
        GL.AttachShader(h_program, h_fragment);

        GL.LinkProgram(h_program);

        GL.DetachShader(h_program, h_vertex);
        GL.DeleteShader(h_vertex);
        GL.DetachShader(h_program, h_fragment);
        GL.DeleteShader(h_fragment);

        GL.GetProgram(h_program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0)
        {
            string log = GL.GetProgramInfoLog(h_program);
            GL.DeleteProgram(h_program);

            throw new InvalidOperationException(
                $"Shader program ({fragmentFilename}) link error:\n{log}");
        }

        return h_program;
    }
}
