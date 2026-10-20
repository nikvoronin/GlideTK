using OpenTK.Mathematics;

namespace GlideTK.Scene.Expr;

/// <summary>
/// A number spread over the three components, for "2 * p" and "p / 2"
/// </summary>
public sealed class VecSplatExpr(ExprBase value) : Vec3Expr
{
    readonly ExprBase value = value;

    public override Vector3 Eval(float t) => new(value.Eval(t));
}
