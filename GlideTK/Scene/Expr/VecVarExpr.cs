using OpenTK.Mathematics;

namespace GlideTK.Scene.Expr;

/// <summary>
/// A named vector of 'var', evaluated once per time value like <see cref="VarExpr"/>
/// </summary>
public sealed class VecVarExpr(Vec3Expr value) : Vec3Expr
{
    readonly Vec3Expr value = value;
    float cachedT = float.NaN;
    Vector3 cachedValue;

    public override Vector3 Eval(float t)
    {
        if (t != cachedT)
        {
            cachedValue = value.Eval(t);
            cachedT = t;
        }

        return cachedValue;
    }
}
