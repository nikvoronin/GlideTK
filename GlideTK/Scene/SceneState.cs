using GlideTK.Scene.Glsl;
using GlideTK.Scene.Nodes;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GlideTK.Scene;

/// <summary>
/// The single source of the scene for physics and rendering.
/// Update() compiles the scene at the physics time; both the CPU interpreter
/// and the shader UBO consume the same Data, so they see identical geometry.
/// </summary>
public class SceneState
{
    readonly string path;

    /// <summary>Name of the scene file</summary>
    public string FileName => Path.GetFileName(path);
    List<SceneNode> nodes = [];
    float time;

    public Vector4[] Data { get; private set; }

    /// <summary>Shapes of the scene leaves, 
    /// the fragment shader is generated from them</summary>
    public SceneShapes Shapes { get; private set; } = new();

    /// <summary>
    /// Incremented when the shapes changed (a new scene or a new structure), 
    /// the shader must be generated again.
    /// </summary>
    public int ShaderRevision { get; private set; }

    /// <summary>
    /// Incremented on every Update/Reload, 
    /// used to skip redundant GPU uploads.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Last load error, null when the scene file loaded fine.
    /// </summary>
    public string LastError { get; private set; }

    public SceneState(string path)
    {
        this.path = Path.IsPathRooted(path) 
            ? path 
            : Path.Combine(AppContext.BaseDirectory, path);
        Data = SceneCompiler.Compile(nodes, 0f, Shapes);
        Reload();
    }

    public void Update(float t)
    {
        time = t;

        int shapesVersion = Shapes.Version;
        Data = SceneCompiler.Compile(nodes, t, Shapes);

        if (Shapes.Version != shapesVersion)
            ShaderRevision++;

        Version++;
    }

    /// <summary>
    /// Re-reads the scene file. 
    /// On error keeps the current scene and sets LastError.
    /// </summary>
    public bool Reload()
    {
        try
        {
            List<SceneNode> parsed = SceneParser.Parse(File.ReadAllText(path));
            var parsedShapes = new SceneShapes();
            Vector4[] data = SceneCompiler.Compile(parsed, time, parsedShapes);

            nodes = parsed;
            Data = data;
            Shapes = parsedShapes;
            ShaderRevision++;
            Version++;
            LastError = null;

            return true;
        }
        catch (Exception e) 
            when (e is SceneParseException 
                || e is SceneCompileException 
                || e is IOException 
                || e is UnauthorizedAccessException)
        {
            LastError = $"{Path.GetFileName(path)}{(e is SceneParseException ? "" : ": ")}{e.Message}";

            Debug.WriteLine(LastError);
            Console.Error.WriteLine(LastError);
            
            return false;
        }
    }
}
