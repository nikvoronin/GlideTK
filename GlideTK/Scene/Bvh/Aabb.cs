using OpenTK.Mathematics;
using System;

namespace GlideTK.Scene.Bvh;

/// <summary>
/// Axis aligned bounding box, components may be infinite
/// </summary>
public readonly record struct Aabb(Vector3 Min, Vector3 Max)
{
    public static readonly Aabb Infinite = new(
        new Vector3(float.NegativeInfinity), 
        new Vector3(float.PositiveInfinity));

    public static Aabb Centered(Vector3 half) => 
        new(-half, half);

    public static Aabb Centered(float half) => 
        Centered(new Vector3(half));

    public static Aabb FromPoints(params Vector3[] points)
    {
        Vector3 min = points[0], max = points[0];
        foreach (Vector3 p in points)
        {
            min = Vector3.ComponentMin(min, p);
            max = Vector3.ComponentMax(max, p);
        }
        
        return new Aabb(min, max);
    }

    public bool IsFinite =>
        float.IsFinite(Min.X) 
        && float.IsFinite(Min.Y) 
        && float.IsFinite(Min.Z) 
        && float.IsFinite(Max.X) 
        && float.IsFinite(Max.Y) 
        && float.IsFinite(Max.Z);

    public Vector3 Center => (Min + Max) * 0.5f;

    public float Volume
    {
        get
        {
            Vector3 size = Max - Min;
            return IsFinite ? size.X * size.Y * size.Z : float.PositiveInfinity;
        }
    }

    public Vector3[] Corners() =>
    [
        new(Min.X, Min.Y, Min.Z), new(Max.X, Min.Y, Min.Z),
        new(Min.X, Max.Y, Min.Z), new(Max.X, Max.Y, Min.Z),
        new(Min.X, Min.Y, Max.Z), new(Max.X, Min.Y, Max.Z),
        new(Min.X, Max.Y, Max.Z), new(Max.X, Max.Y, Max.Z),
    ];

    public Aabb Union(Aabb other) => new(
        Vector3.ComponentMin(Min, other.Min), 
        Vector3.ComponentMax(Max, other.Max));

    public Aabb Expand(Vector3 r) => 
        new(Min - r, Max + r);

    public Aabb Expand(float r) => 
        Expand(new Vector3(MathF.Abs(r)));

    public Aabb Translate(Vector3 offset) => 
        new(Min + offset, Max + offset);

    /// <summary>Box of the transformed corners, infinite boxes stay infinite</summary>
    public Aabb Transform(Func<Vector3, Vector3> transform) =>
        IsFinite ? FromPoints(Array.ConvertAll(Corners(), c => transform(c))) : Infinite;

    /// <summary>The smaller of two boxes that both bound a shape</summary>
    public static Aabb Smaller(Aabb a, Aabb b) => 
        b.Volume < a.Volume ? b : a;

    /// <summary>Distance from p to the box, 0 inside</summary>
    public static float Distance(Vector3 p, Vector3 min, Vector3 max) =>
        Sdf.Max(Vector3.ComponentMax(min - p, p - max), 0f).Length;
}
