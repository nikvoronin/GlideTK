using OpenTK.Mathematics;
using System;

namespace GldeTK.Scene;

/// <summary>
/// Signed distance functions from https://iquilezles.org/articles/distfunctions/.
/// Mirror of the sd* functions in shaders/fragment.c: keep the formulas identical,
/// the physics and the picture must agree.
/// </summary>
public static class Sdf
{
    // GLSL built-ins ----------------------------------------------------------------------

    /// <summary>GLSL sign(): 0 for 0 (and NaN), unlike MathF.Sign it never throws</summary>
    public static float Sign(float x) => x > 0f ? 1f : x < 0f ? -1f : 0f;

    public static float Clamp(float x, float a, float b) => MathF.Min(MathF.Max(x, a), b);

    public static Vector2 Abs(Vector2 v) => new(MathF.Abs(v.X), MathF.Abs(v.Y));

    public static Vector3 Abs(Vector3 v) => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));

    public static Vector2 Max(Vector2 v, float s) => new(MathF.Max(v.X, s), MathF.Max(v.Y, s));

    public static Vector3 Max(Vector3 v, float s) => new(MathF.Max(v.X, s), MathF.Max(v.Y, s), MathF.Max(v.Z, s));

    public static float Dot2(Vector2 v) => Vector2.Dot(v, v);

    public static float Dot2(Vector3 v) => Vector3.Dot(v, v);

    public static float Ndot(Vector2 a, Vector2 b) => a.X * b.X - a.Y * b.Y;

    // Primitives ----------------------------------------------------------------------

    public static float Sphere(Vector3 p, float r) => p.Length - r;

    public static float Box(Vector3 p, Vector3 b)
    {
        Vector3 d = Abs(p) - b;
        return MathF.Min(MathF.Max(d.X, MathF.Max(d.Y, d.Z)), 0f) + Max(d, 0f).Length;
    }

    public static float RoundBox(Vector3 p, Vector3 b, float r)
    {
        Vector3 q = Abs(p) - b + new Vector3(r);
        return Max(q, 0f).Length + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0f) - r;
    }

    public static float BoxFrame(Vector3 p, Vector3 b, float e)
    {
        p = Abs(p) - b;
        Vector3 q = Abs(p + new Vector3(e)) - new Vector3(e);
        return MathF.Min(MathF.Min(
            Max(new Vector3(p.X, q.Y, q.Z), 0f).Length + MathF.Min(MathF.Max(p.X, MathF.Max(q.Y, q.Z)), 0f),
            Max(new Vector3(q.X, p.Y, q.Z), 0f).Length + MathF.Min(MathF.Max(q.X, MathF.Max(p.Y, q.Z)), 0f)),
            Max(new Vector3(q.X, q.Y, p.Z), 0f).Length + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, p.Z)), 0f));
    }

    public static float Plane(Vector3 p, Vector3 n, float h) => Vector3.Dot(p, n) - h;

    public static float Torus(Vector3 p, float bigR, float r) =>
        new Vector2(p.Xz.Length - bigR, p.Y).Length - r;

    public static float CappedTorus(Vector3 p, Vector2 sc, float ra, float rb)
    {
        p.X = MathF.Abs(p.X);
        float k = (sc.Y * p.X > sc.X * p.Y) ? Vector2.Dot(p.Xy, sc) : p.Xy.Length;
        return MathF.Sqrt(Vector3.Dot(p, p) + ra * ra - 2f * ra * k) - rb;
    }

    public static float Link(Vector3 p, float le, float r1, float r2)
    {
        var q = new Vector3(p.X, MathF.Max(MathF.Abs(p.Y) - le, 0f), p.Z);
        return new Vector2(q.Xy.Length - r1, q.Z).Length - r2;
    }

    public static float InfCylinder(Vector3 p, float r) => p.Xz.Length - r;

    public static float Cylinder(Vector3 p, float r, float h)
    {
        Vector2 d = Abs(new Vector2(p.Xz.Length, p.Y)) - new Vector2(r, h);
        return MathF.Min(MathF.Max(d.X, d.Y), 0f) + Max(d, 0f).Length;
    }

    public static float SegCylinder(Vector3 p, Vector3 a, Vector3 b, float r)
    {
        Vector3 ba = b - a;
        Vector3 pa = p - a;
        float baba = Vector3.Dot(ba, ba);
        float paba = Vector3.Dot(pa, ba);
        float x = (pa * baba - ba * paba).Length - r * baba;
        float y = MathF.Abs(paba - baba * 0.5f) - baba * 0.5f;
        float x2 = x * x;
        float y2 = y * y * baba;
        float d = (MathF.Max(x, y) < 0f) ? -MathF.Min(x2, y2) : (((x > 0f) ? x2 : 0f) + ((y > 0f) ? y2 : 0f));
        return Sign(d) * MathF.Sqrt(MathF.Abs(d)) / baba;
    }

    public static float RoundCylinder(Vector3 p, float ra, float rb, float h)
    {
        var d = new Vector2(p.Xz.Length - ra + rb, MathF.Abs(p.Y) - h);
        return MathF.Min(MathF.Max(d.X, d.Y), 0f) + Max(d, 0f).Length - rb;
    }

    public static float Cone(Vector3 p, Vector2 c, float h)
    {
        Vector2 q = h * new Vector2(c.X / c.Y, -1f);
        var w = new Vector2(p.Xz.Length, p.Y);
        Vector2 a = w - q * Clamp(Vector2.Dot(w, q) / Vector2.Dot(q, q), 0f, 1f);
        Vector2 b = w - q * new Vector2(Clamp(w.X / q.X, 0f, 1f), 1f);
        float k = Sign(q.Y);
        float d = MathF.Min(Vector2.Dot(a, a), Vector2.Dot(b, b));
        float s = MathF.Max(k * (w.X * q.Y - w.Y * q.X), k * (w.Y - q.Y));
        return MathF.Sqrt(d) * Sign(s);
    }

    public static float InfCone(Vector3 p, Vector2 c)
    {
        var q = new Vector2(p.Xz.Length, -p.Y);
        float d = (q - c * MathF.Max(Vector2.Dot(q, c), 0f)).Length;
        return d * ((q.X * c.Y - q.Y * c.X < 0f) ? -1f : 1f);
    }

    public static float CappedCone(Vector3 p, float h, float r1, float r2)
    {
        var q = new Vector2(p.Xz.Length, p.Y);
        var k1 = new Vector2(r2, h);
        var k2 = new Vector2(r2 - r1, 2f * h);
        var ca = new Vector2(q.X - MathF.Min(q.X, (q.Y < 0f) ? r1 : r2), MathF.Abs(q.Y) - h);
        Vector2 cb = q - k1 + k2 * Clamp(Vector2.Dot(k1 - q, k2) / Dot2(k2), 0f, 1f);
        float s = (cb.X < 0f && ca.Y < 0f) ? -1f : 1f;
        return s * MathF.Sqrt(MathF.Min(Dot2(ca), Dot2(cb)));
    }

    public static float SegCone(Vector3 p, Vector3 a, Vector3 b, float ra, float rb)
    {
        float rba = rb - ra;
        float baba = Vector3.Dot(b - a, b - a);
        float papa = Vector3.Dot(p - a, p - a);
        float paba = Vector3.Dot(p - a, b - a) / baba;
        float x = MathF.Sqrt(papa - paba * paba * baba);
        float cax = MathF.Max(0f, x - ((paba < 0.5f) ? ra : rb));
        float cay = MathF.Abs(paba - 0.5f) - 0.5f;
        float k = rba * rba + baba;
        float f = Clamp((rba * (x - ra) + paba * baba) / k, 0f, 1f);
        float cbx = x - ra - f * rba;
        float cby = paba - f;
        float s = (cbx < 0f && cay < 0f) ? -1f : 1f;
        return s * MathF.Sqrt(MathF.Min(cax * cax + cay * cay * baba, cbx * cbx + cby * cby * baba));
    }

    public static float SolidAngle(Vector3 p, Vector2 c, float ra)
    {
        var q = new Vector2(p.Xz.Length, p.Y);
        float l = q.Length - ra;
        float m = (q - c * Clamp(Vector2.Dot(q, c), 0f, ra)).Length;
        return MathF.Max(l, m * Sign(c.Y * q.X - c.X * q.Y));
    }

    public static float CutSphere(Vector3 p, float r, float h)
    {
        float w = MathF.Sqrt(r * r - h * h);
        var q = new Vector2(p.Xz.Length, p.Y);
        float s = MathF.Max((h - r) * q.X * q.X + w * w * (h + r - 2f * q.Y), h * q.X - w * q.Y);
        return (s < 0f) ? q.Length - r
            : (q.X < w) ? h - q.Y
            : (q - new Vector2(w, h)).Length;
    }

    public static float CutHollowSphere(Vector3 p, float r, float h, float t)
    {
        float w = MathF.Sqrt(r * r - h * h);
        var q = new Vector2(p.Xz.Length, p.Y);
        return ((h * q.X < w * q.Y) ? (q - new Vector2(w, h)).Length : MathF.Abs(q.Length - r)) - t;
    }

    public static float DeathStar(Vector3 p2, float ra, float rb, float d)
    {
        float a = (ra * ra - rb * rb + d * d) / (2f * d);
        float b = MathF.Sqrt(MathF.Max(ra * ra - a * a, 0f));
        var p = new Vector2(p2.X, p2.Yz.Length);
        if (p.X * b - p.Y * a > d * MathF.Max(b - p.Y, 0f))
            return (p - new Vector2(a, b)).Length;
        return MathF.Max(p.Length - ra, -((p - new Vector2(d, 0f)).Length - rb));
    }

    public static float RoundCone(Vector3 p, float r1, float r2, float h)
    {
        float b = (r1 - r2) / h;
        float a = MathF.Sqrt(1f - b * b);
        var q = new Vector2(p.Xz.Length, p.Y);
        float k = Vector2.Dot(q, new Vector2(-b, a));
        if (k < 0f) return q.Length - r1;
        if (k > a * h) return (q - new Vector2(0f, h)).Length - r2;
        return Vector2.Dot(q, new Vector2(a, b)) - r1;
    }

    public static float SegRoundCone(Vector3 p, Vector3 a, Vector3 b, float r1, float r2)
    {
        Vector3 ba = b - a;
        float l2 = Vector3.Dot(ba, ba);
        float rr = r1 - r2;
        float a2 = l2 - rr * rr;
        float il2 = 1f / l2;
        Vector3 pa = p - a;
        float y = Vector3.Dot(pa, ba);
        float z = y - l2;
        float x2 = Dot2(pa * l2 - ba * y);
        float y2 = y * y * l2;
        float z2 = z * z * l2;
        float k = Sign(rr) * rr * rr * x2;
        if (Sign(z) * a2 * z2 > k) return MathF.Sqrt(x2 + z2) * il2 - r2;
        if (Sign(y) * a2 * y2 < k) return MathF.Sqrt(x2 + y2) * il2 - r1;
        return (MathF.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
    }

    public static float Ellipsoid(Vector3 p, Vector3 r)
    {
        float k0 = (p / r).Length;
        float k1 = (p / (r * r)).Length;
        return k0 * (k0 - 1f) / k1;
    }

    public static float Rhombus(Vector3 p, float la, float lb, float h, float ra)
    {
        p = Abs(p);
        var b = new Vector2(la, lb);
        float f = Clamp(Ndot(b, b - 2f * p.Xz) / Vector2.Dot(b, b), -1f, 1f);
        var q = new Vector2(
            (p.Xz - 0.5f * b * new Vector2(1f - f, 1f + f)).Length * Sign(p.X * b.Y + p.Z * b.X - b.X * b.Y) - ra,
            p.Y - h);
        return MathF.Min(MathF.Max(q.X, q.Y), 0f) + Max(q, 0f).Length;
    }

    public static float Octahedron(Vector3 p, float s)
    {
        p = Abs(p);
        float m = p.X + p.Y + p.Z - s;
        Vector3 q;
        if (3f * p.X < m) q = p;
        else if (3f * p.Y < m) q = new Vector3(p.Y, p.Z, p.X);
        else if (3f * p.Z < m) q = new Vector3(p.Z, p.X, p.Y);
        else return m * 0.57735027f;
        float k = Clamp(0.5f * (q.Z - q.Y + s), 0f, s);
        return new Vector3(q.X, q.Y - s + k, q.Z - k).Length;
    }

    public static float Pyramid(Vector3 p, float h)
    {
        float m2 = h * h + 0.25f;
        p.X = MathF.Abs(p.X);
        p.Z = MathF.Abs(p.Z);
        if (p.Z > p.X)
            (p.X, p.Z) = (p.Z, p.X);
        p.X -= 0.5f;
        p.Z -= 0.5f;
        var q = new Vector3(p.Z, h * p.Y - 0.5f * p.X, h * p.X + 0.5f * p.Y);
        float s = MathF.Max(-q.X, 0f);
        float t = Clamp((q.Y - 0.5f * p.Z) / (m2 + 0.25f), 0f, 1f);
        float a = m2 * (q.X + s) * (q.X + s) + q.Y * q.Y;
        float b = m2 * (q.X + 0.5f * t) * (q.X + 0.5f * t) + (q.Y - m2 * t) * (q.Y - m2 * t);
        float d2 = MathF.Min(q.Y, -q.X * m2 - q.Y * 0.5f) > 0f ? 0f : MathF.Min(a, b);
        return MathF.Sqrt((d2 + q.Z * q.Z) / m2) * Sign(MathF.Max(q.Z, -p.Y));
    }

    public static float HexPrism(Vector3 p, Vector2 h)
    {
        var k = new Vector3(-0.8660254f, 0.5f, 0.57735f);
        p = Abs(p);
        Vector2 xy = p.Xy - 2f * MathF.Min(Vector2.Dot(k.Xy, p.Xy), 0f) * k.Xy;
        p.X = xy.X;
        p.Y = xy.Y;
        var d = new Vector2(
            (p.Xy - new Vector2(Clamp(p.X, -k.Z * h.X, k.Z * h.X), h.X)).Length * Sign(p.Y - h.X),
            p.Z - h.Y);
        return MathF.Min(MathF.Max(d.X, d.Y), 0f) + Max(d, 0f).Length;
    }

    public static float TriPrism(Vector3 p, Vector2 h)
    {
        Vector3 q = Abs(p);
        return MathF.Max(q.Z - h.Y, MathF.Max(q.X * 0.866025f + p.Y * 0.5f, -p.Y) - h.X * 0.5f);
    }

    public static float Capsule(Vector3 p, Vector3 a, Vector3 b, float r)
    {
        Vector3 pa = p - a, ba = b - a;
        float h = Clamp(Vector3.Dot(pa, ba) / Vector3.Dot(ba, ba), 0f, 1f);
        return (pa - ba * h).Length - r;
    }

    public static float VerticalCapsule(Vector3 p, float h, float r)
    {
        p.Y -= Clamp(p.Y, 0f, h);
        return p.Length - r;
    }

    public static float Triangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ba = b - a, pa = p - a;
        Vector3 cb = c - b, pb = p - b;
        Vector3 ac = a - c, pc = p - c;
        Vector3 nor = Vector3.Cross(ba, ac);

        return MathF.Sqrt(
            (Sign(Vector3.Dot(Vector3.Cross(ba, nor), pa)) +
             Sign(Vector3.Dot(Vector3.Cross(cb, nor), pb)) +
             Sign(Vector3.Dot(Vector3.Cross(ac, nor), pc)) < 2f)
            ?
            MathF.Min(MathF.Min(
                Dot2(ba * Clamp(Vector3.Dot(ba, pa) / Dot2(ba), 0f, 1f) - pa),
                Dot2(cb * Clamp(Vector3.Dot(cb, pb) / Dot2(cb), 0f, 1f) - pb)),
                Dot2(ac * Clamp(Vector3.Dot(ac, pc) / Dot2(ac), 0f, 1f) - pc))
            :
            Vector3.Dot(nor, pa) * Vector3.Dot(nor, pa) / Dot2(nor));
    }

    public static float Quad(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vector3 ba = b - a, pa = p - a;
        Vector3 cb = c - b, pb = p - b;
        Vector3 dc = d - c, pc = p - c;
        Vector3 ad = a - d, pd = p - d;
        Vector3 nor = Vector3.Cross(ba, ad);

        return MathF.Sqrt(
            (Sign(Vector3.Dot(Vector3.Cross(ba, nor), pa)) +
             Sign(Vector3.Dot(Vector3.Cross(cb, nor), pb)) +
             Sign(Vector3.Dot(Vector3.Cross(dc, nor), pc)) +
             Sign(Vector3.Dot(Vector3.Cross(ad, nor), pd)) < 3f)
            ?
            MathF.Min(MathF.Min(MathF.Min(
                Dot2(ba * Clamp(Vector3.Dot(ba, pa) / Dot2(ba), 0f, 1f) - pa),
                Dot2(cb * Clamp(Vector3.Dot(cb, pb) / Dot2(cb), 0f, 1f) - pb)),
                Dot2(dc * Clamp(Vector3.Dot(dc, pc) / Dot2(dc), 0f, 1f) - pc)),
                Dot2(ad * Clamp(Vector3.Dot(ad, pd) / Dot2(ad), 0f, 1f) - pd))
            :
            Vector3.Dot(nor, pa) * Vector3.Dot(nor, pa) / Dot2(nor));
    }
}
