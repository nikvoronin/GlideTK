using OpenTK.Mathematics;
using System.Collections.Generic;
using System.Linq;

namespace GlideTK.Scene.Glsl;

/// <summary>
/// Distinct instruction sequences of the scene leaves. 
/// A shape is the opcodes and the instruction lengths only, 
/// the argument values are read from the program at run time, 
/// so the scene animated by t keeps its shapes 
/// and the shader generated from them stays valid.
/// </summary>
public sealed class SceneShapes
{
    readonly Dictionary<string, int> ids = [];
    readonly List<(int Op, int Length)[]> shapes = [];
    readonly List<int> always = [];

    /// <summary>
    /// Changes whenever a shape 
    /// or the unbounded leaves are added or changed
    /// </summary>
    public int Version { get; private set; }

    public IReadOnlyList<(int Op, int Length)[]> Shapes => shapes;

    /// <summary>
    /// Shapes of the unbounded leaves in the program order, 
    /// they are evaluated everywhere
    /// </summary>
    public IReadOnlyList<int> Always => always;

    /// <summary>
    /// Returns the id of the shape of the leaf code, 
    /// registers a new shape
    /// </summary>
    public int Register(IReadOnlyList<Vector4> code)
    {
        var instructions = new List<(int Op, int Length)>();
        for (int pc = 0; pc < code.Count; pc += (int)code[pc].Y)
            instructions.Add(((int)code[pc].X, (int)code[pc].Y));

        string key = string.Join(';', instructions.Select(i => $"{i.Op}:{i.Length}"));
        if (ids.TryGetValue(key, out int id))
            return id;

        id = shapes.Count;
        ids[key] = id;
        shapes.Add([.. instructions]);
        Version++;

        return id;
    }

    public void SetAlways(IEnumerable<int> shapeIds)
    {
        int[] next = [.. shapeIds];
        if (always.SequenceEqual(next))
            return;

        always.Clear();
        always.AddRange(next);
        Version++;
    }
}
