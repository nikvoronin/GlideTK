using GlideTK.Scene.Bvh;
using OpenTK.Mathematics;
using System;

namespace GlideTK.Scene;

/// <summary>
/// CPU interpreter of a compiled scene program. Mirror of map() in shaders/trace.c 
/// and of the generated shape functions (Glsl/GlslOps.cs): 
/// any change here must be repeated there.
/// </summary>
public static class SceneVM
{
    static Vector2 Union(Vector2 a, Vector2 b) => 
        a.X < b.X ? a : b;

    /// <summary>
    /// A box can lower the distance only when it is closer 
    /// than the best distance or contains the point: 
    /// inside the box the distance can be negative
    /// </summary>
    static bool MayContribute(float boxDistance, float best) => 
        boxDistance <= 0f 
        || boxDistance < best;

    static float BoxDistance(Vector4[] program, int bvh, int node, Vector3 p) =>
        Aabb.Distance(
            p, 
            program[bvh + 2 * node].Xyz, 
            program[bvh + 2 * node + 1].Xyz);

    /// <summary>Distance from pos to the scene surface.</summary>
    public static float Eval(Vector4[] program, Vector3 pos)
    {
        Vector4 header = program[0];
        var best = new Vector2(SceneOpcodes.EMPTY_DISTANCE, 0f);

        // unbounded leaves
        int alwaysCount = (int)header.W;
        for (int i = 0; i < alwaysCount; i++)
        {
            best = Union(best, EvalLeaf(program, (int)program[1 + i].X, (int)program[1 + i].Y, pos));
        }

        // bounded leaves: skip boxes not closer than the best distance found so far
        int bvh = (int)header.Y;
        int nodeCount = (int)header.Z;
        Span<int> stack = stackalloc int[Const.BVH_STACK_DEPTH];
        int sp = 0;
        if (nodeCount > 0)
            stack[sp++] = 0;

        while (sp > 0)
        {
            int node = stack[--sp];
            Vector4 lo = program[bvh + 2 * node];
            Vector4 hi = program[bvh + 2 * node + 1];

            if (!MayContribute(Aabb.Distance(pos, lo.Xyz, hi.Xyz), best.X))
                continue;

            if (lo.W < 0f)
            {
                best = Union(best, EvalLeaf(program, (int)-lo.W, (int)hi.W, pos));
                continue;
            }

            // visit the nearer child first: push it last
            int left = (int)lo.W, right = (int)hi.W;
            float dl = BoxDistance(program, bvh, left, pos);
            float dr = BoxDistance(program, bvh, right, pos);
            int near = dl < dr ? left : right, far = dl < dr ? right : left;
            float dNear = MathF.Min(dl, dr), dFar = MathF.Max(dl, dr);

            if (MayContribute(dFar, best.X))
                stack[sp++] = far;
            
            if (MayContribute(dNear, best.X))
                stack[sp++] = near;
        }

        return best.X;
    }

    /// <summary>Runs the stack program of a leaf in [start, end), returns (distance, material)</summary>
    static Vector2 EvalLeaf(Vector4[] program, int start, int end, Vector3 pos)
    {
        Span<Vector4> ps = stackalloc Vector4[Const.SCENE_STACK_DEPTH];   // xyz position, w scale
        Span<Vector2> ds = stackalloc Vector2[Const.SCENE_STACK_DEPTH];   // x distance, y material
        int pp = 0, dp = 0;
        ps[0] = new Vector4(pos, 1f);

        int pc = start;

        while (pc < end)
        {
            Vector4 h = program[pc];
            int op = (int)h.X;
            Vector3 p = ps[pp].Xyz;
            float s = ps[pp].W;

            if (op < SceneOpcodes.DOMAIN_FIRST)
            {
                ds[dp++] = new Vector2(Primitive(op, program, pc, p) * s, h.Z);
            }
            else if (op == SceneOpcodes.POPPOS)
            {
                pp--;
            }
            else if (op < SceneOpcodes.POST_FIRST)
            {
                ps[pp + 1] = Domain(op, program, pc, p, s);
                pp++;
            }
            else if (op < SceneOpcodes.FOLD_FIRST)
            {
                ds[dp - 1] = Post(op, program, pc, ds[dp - 1], p, s);
            }
            else
            {
                float k = h.Y > 1f ? program[pc + 1].X * s : 0f;
                Vector2 b = ds[--dp];
                ds[dp - 1] = Fold(op, ds[dp - 1], b, k);
            }

            pc += (int)h.Y;
        }

        return ds[0];
    }

    static float Primitive(int op, Vector4[] program, int pc, Vector3 p)
    {
        Vector4 S = program[pc + 1];
        Vector3 V(int i) => program[pc + 2 + i].Xyz;

        return op switch
        {
            SceneOpcodes.SPHERE => Sdf.Sphere(p, S.X),
            SceneOpcodes.BOX => Sdf.Box(p, V(0)),
            SceneOpcodes.ROUNDBOX => Sdf.RoundBox(p, V(0), S.X),
            SceneOpcodes.BOXFRAME => Sdf.BoxFrame(p, V(0), S.X),
            SceneOpcodes.PLANE => Sdf.Plane(p, V(0), S.X),
            SceneOpcodes.TORUS => Sdf.Torus(p, S.X, S.Y),
            SceneOpcodes.CAPPEDTORUS => Sdf.CappedTorus(p, S.Xy, S.Z, S.W),
            SceneOpcodes.LINK => Sdf.Link(p, S.X, S.Y, S.Z),
            SceneOpcodes.INFCYLINDER => Sdf.InfCylinder(p, S.X),
            SceneOpcodes.CYLINDER => Sdf.Cylinder(p, S.X, S.Y),
            SceneOpcodes.SEGCYLINDER => Sdf.SegCylinder(p, V(0), V(1), S.X),
            SceneOpcodes.ROUNDCYLINDER => Sdf.RoundCylinder(p, S.X, S.Y, S.Z),
            SceneOpcodes.CONE => Sdf.Cone(p, S.Xy, S.Z),
            SceneOpcodes.INFCONE => Sdf.InfCone(p, S.Xy),
            SceneOpcodes.CAPPEDCONE => Sdf.CappedCone(p, S.X, S.Y, S.Z),
            SceneOpcodes.SEGCONE => Sdf.SegCone(p, V(0), V(1), S.X, S.Y),
            SceneOpcodes.SOLIDANGLE => Sdf.SolidAngle(p, S.Xy, S.Z),
            SceneOpcodes.CUTSPHERE => Sdf.CutSphere(p, S.X, S.Y),
            SceneOpcodes.CUTHOLLOWSPHERE => Sdf.CutHollowSphere(p, S.X, S.Y, S.Z),
            SceneOpcodes.DEATHSTAR => Sdf.DeathStar(p, S.X, S.Y, S.Z),
            SceneOpcodes.ROUNDCONE => Sdf.RoundCone(p, S.X, S.Y, S.Z),
            SceneOpcodes.SEGROUNDCONE => Sdf.SegRoundCone(p, V(0), V(1), S.X, S.Y),
            SceneOpcodes.ELLIPSOID => Sdf.Ellipsoid(p, V(0)),
            SceneOpcodes.RHOMBUS => Sdf.Rhombus(p, S.X, S.Y, S.Z, S.W),
            SceneOpcodes.OCTAHEDRON => Sdf.Octahedron(p, S.X),
            SceneOpcodes.PYRAMID => Sdf.Pyramid(p, S.X),
            SceneOpcodes.HEXPRISM => Sdf.HexPrism(p, S.Xy),
            SceneOpcodes.TRIPRISM => Sdf.TriPrism(p, S.Xy),
            SceneOpcodes.CAPSULE => Sdf.Capsule(p, V(0), V(1), S.X),
            SceneOpcodes.VCAPSULE => Sdf.VerticalCapsule(p, S.X, S.Y),
            SceneOpcodes.TRIANGLE => Sdf.Triangle(p, V(0), V(1), V(2)),
            SceneOpcodes.QUAD => Sdf.Quad(p, V(0), V(1), V(2), V(3)),
            SceneOpcodes.MANDELBULB => Sdf.Mandelbulb(p, S.X, S.Y),
            SceneOpcodes.MENGERSPONGE => Sdf.MengerSponge(p, S.X, S.Y),

            _ => throw new InvalidOperationException($"Bad scene opcode {op} at {pc}"),
        };
    }

    /// <summary>mod(p, c) - c/2, zero period is not repeated</summary>
    static float Rep(float p, float c) => 
        c != 0f ? Sdf.Mod(p, c) - 0.5f * c : p;

    /// <summary>p - c * clamp(round(p / c), -l, l), zero period is not repeated</summary>
    static float RepLimited(float p, float c, float l) =>
        c != 0f ? p - c * Sdf.Clamp(MathF.Floor(p / c + 0.5f), -l, l) : p;

    static float Mirror(float p, float axis) => 
        axis != 0f ? MathF.Abs(p) : p;

    /// <summary>Returns the new (position, scale)</summary>
    static Vector4 Domain(int op, Vector4[] program, int pc, Vector3 p, float s)
    {
        Vector4 S = program[pc + 1];
        Vector3 V(int i) => program[pc + 2 + i].Xyz;

        switch (op)
        {
            case SceneOpcodes.TRANSLATE:
                return new Vector4(p - V(0), s);

            case SceneOpcodes.REPEAT:
                Vector3 c = V(0);
                return new Vector4(
                    Rep(p.X, c.X), 
                    Rep(p.Y, c.Y), 
                    Rep(p.Z, c.Z), 
                    s);

            case SceneOpcodes.REPEAT_LIMITED:
                Vector3 period = V(0), limit = V(1);
                return new Vector4(
                    RepLimited(p.X, period.X, limit.X),
                    RepLimited(p.Y, period.Y, limit.Y),
                    RepLimited(p.Z, period.Z, limit.Z),
                    s);

            case SceneOpcodes.ROTATE:
                return new Vector4(
                    Vector3.Dot(V(0), p),
                    Vector3.Dot(V(1), p),
                    Vector3.Dot(V(2), p),
                    s);

            case SceneOpcodes.SCALE:
                return new Vector4(p / S.X, s * S.X);

            case SceneOpcodes.MIRROR:
                Vector3 axes = V(0);
                return new Vector4(
                    Mirror(p.X, axes.X),
                    Mirror(p.Y, axes.Y), 
                    Mirror(p.Z, axes.Z), 
                    s);

            case SceneOpcodes.ELONGATE:
                Vector3 e = V(0);
                return new Vector4(
                    p.X - Sdf.Clamp(p.X, -e.X, e.X),
                    p.Y - Sdf.Clamp(p.Y, -e.Y, e.Y),
                    p.Z - Sdf.Clamp(p.Z, -e.Z, e.Z),
                    s);

            case SceneOpcodes.TWIST:
            {
                float a = S.X * p.Y;
                float co = MathF.Cos(a), 
                    si = MathF.Sin(a);

                return new Vector4(
                    co * p.X - si * p.Z, 
                    p.Y, 
                    si * p.X + co * p.Z, 
                    s);
            }

            case SceneOpcodes.BEND:
            {
                float a = S.X * p.X;
                float co = MathF.Cos(a), 
                    si = MathF.Sin(a);

                return new Vector4(
                    co * p.X - si * p.Y, 
                    si * p.X + co * p.Y, 
                    p.Z, 
                    s);
            }

            case SceneOpcodes.REVOLVE:
                return new Vector4(
                    p.Xz.Length - S.X, 
                    p.Y, 
                    0f, 
                    s);

            case SceneOpcodes.EXTRUDE_SLICE:
                return new Vector4(
                    p.X, 
                    p.Y, 
                    0f, 
                    s);

            default:
                throw new InvalidOperationException(
                    $"Bad scene opcode {op} at {pc}");
        }
    }

    /// <summary>Modifies the top distance, p and s are the block's position and scale</summary>
    static Vector2 Post(
        int op, 
        Vector4[] program, 
        int pc, 
        Vector2 d, 
        Vector3 p, 
        float s)
    {
        Vector4 S = program[pc + 1];

        switch (op)
        {
            case SceneOpcodes.ROUND:
                return new Vector2(d.X - S.X * s, d.Y);

            case SceneOpcodes.ONION:
                return new Vector2(MathF.Abs(d.X) - S.X * s, d.Y);

            case SceneOpcodes.DISPLACE:
                return new Vector2(
                    d.X + S.X * s * MathF.Sin(S.Y * p.X) * MathF.Sin(S.Y * p.Y) * MathF.Sin(S.Y * p.Z),
                    d.Y);

            case SceneOpcodes.EXTRUDE:
                var w = new Vector2(d.X, (MathF.Abs(p.Z) - S.X) * s);

                return new Vector2(
                    MathF.Min(MathF.Max(w.X, w.Y), 0f) 
                        + Sdf.Max(w, 0f).Length, 
                    d.Y);

            default:
                throw new InvalidOperationException(
                    $"Bad scene opcode {op} at {pc}");
        }
    }

    static Vector2 SmoothMin(Vector2 a, Vector2 b, float k)
    {
        if (k <= 0f)
            return a.X < b.X ? a : b;

        float h = Sdf.Clamp(0.5f + 0.5f * (b.X - a.X) / k, 0f, 1f);
        float d = b.X + (a.X - b.X) * h - k * h * (1f - h);

        return new Vector2(d, h > 0.5f ? a.Y : b.Y);
    }

    static Vector2 Fold(int op, Vector2 a, Vector2 b, float k)
    {
        switch (op)
        {
            case SceneOpcodes.UNION:
                return a.X < b.X ? a : b;

            case SceneOpcodes.SUBTRACT:
                return new Vector2(MathF.Max(a.X, -b.X), a.Y);

            case SceneOpcodes.INTERSECT:
                return a.X > b.X ? a : b;

            case SceneOpcodes.XOR:
                return new Vector2(
                    MathF.Max(
                        MathF.Min(a.X, b.X), 
                        -MathF.Max(a.X, b.X)),
                    a.X < b.X ? a.Y : b.Y);

            case SceneOpcodes.SMOOTH_UNION:
                return SmoothMin(a, b, k);

            case SceneOpcodes.SMOOTH_SUBTRACT:
                return new Vector2(
                    -SmoothMin(new Vector2(-a.X, a.Y), b, k).X, 
                    a.Y);

            case SceneOpcodes.SMOOTH_INTERSECT:
                Vector2 r = SmoothMin(
                    new Vector2(-a.X, a.Y), 
                    new Vector2(-b.X, b.Y), 
                    k);

                return new Vector2(-r.X, r.Y);

            default:
                throw new InvalidOperationException($"Bad scene opcode {op}");
        }
    }
}
