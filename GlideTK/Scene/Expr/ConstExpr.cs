namespace GlideTK.Scene.Expr;

public sealed class ConstExpr(float value) : ExprBase
{
    readonly float value = value;

    public override float Eval(float t) => value;
}
