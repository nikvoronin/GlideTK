namespace GlideTK.Scene.Expr;

/// <summary>
/// A named expression of 'var', evaluated once per time value:
/// a chain of variables does not repeat its work for every use
/// </summary>
public sealed class VarExpr(ExprBase value) : ExprBase
{
    readonly ExprBase value = value;
    float cachedT = float.NaN, cachedValue;

    public override float Eval(float t)
    {
        if (t != cachedT)
        {
            cachedValue = value.Eval(t);
            cachedT = t;
        }

        return cachedValue;
    }
}
