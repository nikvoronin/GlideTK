using OpenTK.Mathematics;
using System;

namespace GldeTK.Scene;

/// <summary>
/// CPU interpreter of a compiled scene program. Mirror of map() in shaders/fragment.c:
/// any change here must be repeated there.
/// </summary>
public static class SceneVM
{
    // Signed Distance Functions ----------------------------------------------------------------------

    static float SdSphere(Vector3 p, float s) => p.Length - s;

    static float SdBox(Vector3 p, Vector3 b)
    {
        Vector3 d = Abs(p) - b;
        return MathF.Min(MathF.Max(d.X, MathF.Max(d.Y, d.Z)), 0f) +
               Vector3.ComponentMax(d, Vector3.Zero).Length;
    }

    static float SdPlane(Vector3 p, Vector3 n, float dist) => 
        Vector3.Dot(p, n) - dist;

    static float SdCylinder(Vector3 p, float r, float halfHeight) =>
        MathF.Max(p.Xz.Length - r, MathF.Abs(p.Y) - halfHeight);

    static float SdTorus(Vector3 p, float bigR, float smallR) =>
        new Vector2(p.Xz.Length - bigR, p.Y).Length - smallR;

    // Domain operations ----------------------------------------------------------------------

    static Vector3 Abs(Vector3 v) => 
        new(
            MathF.Abs(v.X), 
            MathF.Abs(v.Y), 
            MathF.Abs(v.Z));

    /// <summary>GLSL mod(): x - y * floor(x / y)</summary>
    static float Mod(float x, float y) => 
        x - y * MathF.Floor(x / y);

    /// <summary>mod(p, c) - c/2 per axis, an axis with zero period is left as is</summary>
    static float Rep(float p, float c) => 
        c != 0f ? Mod(p, c) - 0.5f * c : p;

    static Vector2 SmoothMin(Vector2 a, Vector2 b, float k)
    {
        if (k <= 0f)
        {
            return a.X < b.X ? a : b;
        }

        float h = MathHelper.Clamp(0.5f + 0.5f * (b.X - a.X) / k, 0f, 1f);
        float d = b.X + (a.X - b.X) * h - k * h * (1f - h);
        
        return new Vector2(d, h > 0.5f ? a.Y : b.Y);
    }

    // Interpreter ----------------------------------------------------------------------

    /// <summary>Distance from pos to the scene surface.</summary>
    public static float Eval(Vector4[] program, Vector3 pos)
    {
        Span<Vector4> ps = stackalloc Vector4[Const.SCENE_STACK_DEPTH];   // xyz position, w scale
        Span<Vector2> ds = stackalloc Vector2[Const.SCENE_STACK_DEPTH];   // x distance, y material
        int pp = 0, dp = 0;
        ps[0] = new Vector4(pos, 1f);

        int end = (int)program[0].X;
        int pc = 1;

        while (pc < end)
        {
            Vector4 h = program[pc];
            Vector3 p = ps[pp].Xyz;
            float s = ps[pp].W;

            switch ((int)h.X)
            {
                case SceneOpcodes.SPHERE:
                    ds[dp++] = new Vector2(SdSphere(p, h.Y) * s, h.Z);
                    pc += 1;
                    break;

                case SceneOpcodes.BOX:
                    ds[dp++] = new Vector2(SdBox(p, program[pc + 1].Xyz) * s, h.Z);
                    pc += 2;
                    break;
                case SceneOpcodes.PLANE:
                    ds[dp++] = new Vector2(SdPlane(p, program[pc + 1].Xyz, h.Y) * s, h.Z);
                    pc += 2;
                    break;

                case SceneOpcodes.CYLINDER:
                    ds[dp++] = new Vector2(SdCylinder(p, h.Y, h.W) * s, h.Z);
                    pc += 1;
                    break;

                case SceneOpcodes.TORUS:
                    ds[dp++] = new Vector2(SdTorus(p, h.Y, h.W) * s, h.Z);
                    pc += 1;
                    break;

                case SceneOpcodes.TRANSLATE:
                    ps[++pp] = new Vector4(p - h.Yzw, s);
                    pc += 1;
                    break;

                case SceneOpcodes.REPEAT:
                    ps[++pp] = new Vector4(Rep(p.X, h.Y), Rep(p.Y, h.Z), Rep(p.Z, h.W), s);
                    pc += 1;
                    break;

                case SceneOpcodes.ROTATE:
                    ps[++pp] = new Vector4(
                        Vector3.Dot(program[pc + 1].Xyz, p),
                        Vector3.Dot(program[pc + 2].Xyz, p),
                        Vector3.Dot(program[pc + 3].Xyz, p),
                        s);
                    pc += 4;
                    break;

                case SceneOpcodes.SCALE:
                    ps[++pp] = new Vector4(p / h.Y, s * h.Y);
                    pc += 1;
                    break;

                case SceneOpcodes.POPPOS:
                    pp--;
                    pc += 1;
                    break;

                case SceneOpcodes.UNION:
                case SceneOpcodes.SUBTRACT:
                case SceneOpcodes.INTERSECT:
                case SceneOpcodes.SMOOTH:
                    Vector2 b = ds[--dp];
                    Vector2 a = ds[dp - 1];
                    ds[dp - 1] = Combine((int)h.X, a, b, h.Y);
                    pc += 1;
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Bad scene opcode {h.X} at {pc}");
            }
        }

        return dp > 0 ? ds[0].X : SceneOpcodes.EMPTY_DISTANCE;
    }

    static Vector2 Combine(int op, Vector2 a, Vector2 b, float k) =>
        op switch
        {
            SceneOpcodes.UNION => a.X < b.X ? a : b,
            SceneOpcodes.SUBTRACT => new Vector2(MathF.Max(a.X, -b.X), a.Y),
            SceneOpcodes.INTERSECT => a.X > b.X ? a : b,

            _ => SmoothMin(a, b, k),
        };
}
