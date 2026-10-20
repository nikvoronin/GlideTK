using OpenTK.Mathematics;

namespace GlideTK.Scene.Expr;

/// <summary>(x, y, z)</summary>
public sealed class Vec3LiteralExpr(
    ExprBase x, 
    ExprBase y, 
    ExprBase z) 
    : Vec3Expr
{
    readonly ExprBase x = x, y = y, z = z;

    public override Vector3 Eval(float t) =>
        new(x.Eval(t), y.Eval(t), z.Eval(t));
}
