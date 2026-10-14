namespace GlideTK.Scene.Expr;

public sealed class NegExpr(ExprBase operand) : ExprBase
{
    readonly ExprBase operand = operand;

    public override float Eval(float t) => -operand.Eval(t);
}
