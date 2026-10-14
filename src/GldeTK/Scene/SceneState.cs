using GldeTK.Scene.Nodes;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GldeTK.Scene;

/// <summary>
/// The single source of the scene for physics and rendering.
/// Update() compiles the scene at the physics time; both the CPU interpreter
/// and the shader UBO consume the same Data, so they see identical geometry.
/// </summary>
public class SceneState
{
    readonly string path;
    List<SceneNode> nodes = [];
    float time;

    public Vector4[] Data { get; private set; }

    /// <summary>Incremented on every Update/Reload, used to skip redundant GPU uploads.</summary>
    public int Version { get; private set; }

    /// <summary>Last load error, null when the scene file loaded fine.</summary>
    public string LastError { get; private set; }

    public SceneState(string path)
    {
        this.path = Path.IsPathRooted(path) 
            ? path 
            : Path.Combine(AppContext.BaseDirectory, path);
        Data = SceneCompiler.Compile(nodes, 0f);
        Reload();
    }

    public void Update(float t)
    {
        time = t;
        Data = SceneCompiler.Compile(nodes, t);
        Version++;
    }

    /// <summary>
    /// Re-reads the scene file. On error keeps the current scene and sets LastError.
    /// </summary>
    public bool Reload()
    {
        try
        {
            List<SceneNode> parsed = SceneParser.Parse(File.ReadAllText(path));
            Vector4[] data = SceneCompiler.Compile(parsed, time);

            nodes = parsed;
            Data = data;
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
