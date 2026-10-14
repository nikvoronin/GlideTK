using OpenTK.Mathematics;
using System;

namespace GlideTK.Scene.Bvh;

/// <summary>
/// Conservative bounding boxes of scene instructions, in the local space of the instruction.
/// The scene distance of a shape is never less than the distance to its box,
/// which makes skipping far boxes exact (see SceneVM.Eval).
/// Arguments are the evaluated instruction arguments, see <see cref="SceneOpcodes"/>.
/// </summary>
public static class SceneBounds
{
    static Aabb Segment(Vector3 a, Vector3 b, float r) => 
        Aabb.FromPoints(a, b).Expand(r);

    public static Aabb Primitive(int op, float[] s, Vector3[] v)
    {
        float S(int i) => s[i];

        switch (op)
        {
            case SceneOpcodes.SPHERE:
                return Aabb.Centered(S(0));

            case SceneOpcodes.BOX:
            case SceneOpcodes.ROUNDBOX:
            case SceneOpcodes.BOXFRAME:
            case SceneOpcodes.ELLIPSOID:
                return Aabb.Centered(Sdf.Abs(v[0]));

            case SceneOpcodes.TORUS:
                return Aabb.Centered(new Vector3(S(0) + S(1), S(1), S(0) + S(1)));

            case SceneOpcodes.CAPPEDTORUS:      // ring in the XY plane
                return Aabb.Centered(new Vector3(S(2) + S(3), S(2) + S(3), S(3)));

            case SceneOpcodes.LINK:
                return Aabb.Centered(new Vector3(S(1) + S(2), S(0) + S(1) + S(2), S(2)));

            case SceneOpcodes.CYLINDER:
                return Aabb.Centered(new Vector3(S(0), S(1), S(0)));

            case SceneOpcodes.SEGCYLINDER:
            case SceneOpcodes.CAPSULE:
                return Segment(v[0], v[1], S(0));

            case SceneOpcodes.ROUNDCYLINDER:
                return Aabb.Centered(new Vector3(S(0) + S(1), S(2) + S(1), S(0) + S(1)));

            case SceneOpcodes.CONE:             // apex at the origin, base at y = -h
            {
                float h = S(2), r = MathF.Abs(h * S(0) / S(1));
                return new Aabb(
                    new Vector3(-r, -MathF.Abs(h), -r), 
                    new Vector3(r, 0f, r));
            }

            case SceneOpcodes.CAPPEDCONE:
            {
                float r = MathF.Max(S(1), S(2));
                return Aabb.Centered(new Vector3(r, S(0), r));
            }

            case SceneOpcodes.SEGCONE:
            case SceneOpcodes.SEGROUNDCONE:
                return Segment(v[0], v[1], MathF.Max(S(0), S(1)));

            case SceneOpcodes.SOLIDANGLE:
                return Aabb.Centered(S(2));

            case SceneOpcodes.CUTSPHERE:
            case SceneOpcodes.DEATHSTAR:
            case SceneOpcodes.OCTAHEDRON:
                return Aabb.Centered(S(0));

            case SceneOpcodes.CUTHOLLOWSPHERE:
                return Aabb.Centered(S(0) + S(2));

            case SceneOpcodes.ROUNDCONE:
            {
                float r = MathF.Max(S(0), S(1));
                return new Aabb(
                    new Vector3(-r, -S(0), -r), 
                    new Vector3(r, S(2) + S(1), r));
            }

            case SceneOpcodes.RHOMBUS:
                return Aabb.Centered(new Vector3(S(0) + S(3), S(2), S(1) + S(3)));

            case SceneOpcodes.PYRAMID:
                return new Aabb(
                    new Vector3(-0.5f, MathF.Min(0f, S(0)), -0.5f), 
                    new Vector3(0.5f, MathF.Max(0f, S(0)), 0.5f));

            case SceneOpcodes.HEXPRISM:         // circumradius of the hexagon
                return Aabb.Centered(new Vector3(S(0) * 1.1547006f, S(0) * 1.1547006f, S(1)));

            case SceneOpcodes.TRIPRISM:
                return Aabb.Centered(new Vector3(S(0), S(0), S(1)));

            case SceneOpcodes.VCAPSULE:
                return new Aabb(
                    new Vector3(-S(1), -S(1), -S(1)), 
                    new Vector3(S(1), S(0) + S(1), S(1)));

            case SceneOpcodes.TRIANGLE:
            case SceneOpcodes.QUAD:
                return Aabb.FromPoints(v);

            default:    // PLANE, INFCYLINDER, INFCONE
                return Aabb.Infinite;
        }
    }

    /// <summary>Box of a domain operation applied to a child box</summary>
    public static Aabb Domain(int op, float[] s, Vector3[] v, Aabb child)
    {
        switch (op)
        {
            case SceneOpcodes.TRANSLATE:
                return child.Translate(v[0]);

            case SceneOpcodes.REPEAT:
                return v[0] == Vector3.Zero ? child : Aabb.Infinite;

            case SceneOpcodes.REPEAT_LIMITED:
                return child.Expand(Sdf.Abs(v[0]) * Sdf.Abs(v[1]));

            case SceneOpcodes.ROTATE:   // v are the rows of inverse(R), R * x = sum of rows scaled by x
                return child.Transform(c => v[0] * c.X + v[1] * c.Y + v[2] * c.Z);

            case SceneOpcodes.SCALE:
                return child.Transform(c => c * s[0]);

            case SceneOpcodes.MIRROR:
            {
                Vector3 m = Vector3.ComponentMax(Sdf.Abs(child.Min), Sdf.Abs(child.Max));
                Vector3 axes = v[0];
                return new Aabb(
                    new Vector3(axes.X != 0f ? -m.X : child.Min.X, axes.Y != 0f ? -m.Y : child.Min.Y, axes.Z != 0f ? -m.Z : child.Min.Z),
                    new Vector3(axes.X != 0f ? m.X : child.Max.X, axes.Y != 0f ? m.Y : child.Max.Y, axes.Z != 0f ? m.Z : child.Max.Z));
            }

            case SceneOpcodes.ELONGATE:
                return child.Expand(Sdf.Abs(v[0]));

            case SceneOpcodes.TWIST:    // rotation around Y keeps the radius in XZ
            {
                if (!child.IsFinite)
                    return Aabb.Infinite;

                float r = 0f;
                foreach (Vector3 c in child.Corners())
                {
                    r = MathF.Max(r, c.Xz.Length);
                }

                return new Aabb(
                    new Vector3(-r, child.Min.Y, -r), 
                    new Vector3(r, child.Max.Y, r));
            }

            case SceneOpcodes.BEND:     // rotation in XY keeps the radius in XY
            {
                if (!child.IsFinite)
                    return Aabb.Infinite;

                float r = 0f;

                foreach (Vector3 c in child.Corners())
                {
                    r = MathF.Max(r, c.Xy.Length);
                }

                return new Aabb(
                    new Vector3(-r, -r, child.Min.Z), 
                    new Vector3(r, r, child.Max.Z));
            }

            case SceneOpcodes.REVOLVE:  // child x is the radius minus o
            {
                float r = MathF.Max(
                    MathF.Abs(child.Min.X + s[0]), 
                    MathF.Abs(child.Max.X + s[0]));

                return new Aabb(
                    new Vector3(-r, child.Min.Y, -r), 
                    new Vector3(r, child.Max.Y, r));
            }

            case SceneOpcodes.EXTRUDE_SLICE:    // s are the extrude arguments: half length
            {
                float h = MathF.Abs(s[0]);
                return new Aabb(
                    new Vector3(child.Min.X, child.Min.Y, -h), 
                    new Vector3(child.Max.X, child.Max.Y, h));
            }

            default:
                return Aabb.Infinite;
        }
    }

    /// <summary>Box of a distance operation applied to a child box</summary>
    public static Aabb Post(int op, float[] s, Aabb child) =>
        op switch
        {
            SceneOpcodes.ROUND 
            or SceneOpcodes.ONION 
            or SceneOpcodes.DISPLACE => child.Expand(s[0]),

            SceneOpcodes.EXTRUDE => child,

            _ => Aabb.Infinite,
        };

    /// <summary>Box of a fold of two shapes</summary>
    public static Aabb Fold(int op, float k, Aabb a, Aabb b) =>
        op switch
        {
            SceneOpcodes.UNION or SceneOpcodes.XOR => a.Union(b),
            SceneOpcodes.SUBTRACT or SceneOpcodes.SMOOTH_SUBTRACT => a,
            SceneOpcodes.INTERSECT or SceneOpcodes.SMOOTH_INTERSECT => Aabb.Smaller(a, b),
            SceneOpcodes.SMOOTH_UNION => a.Union(b).Expand(k * 0.25f),    // polynomial smin bulges by at most k/4

            _ => Aabb.Infinite,
        };
}
