using OpenTK.Mathematics;

namespace GldeTK.Scene.Expr;

public sealed class Vec3Expr(ExprBase x, ExprBase y, ExprBase z)
{
    public readonly ExprBase X = x, Y = y, Z = z;

    public Vector3 Eval(float t) => 
        new(X.Eval(t), Y.Eval(t), Z.Eval(t));
}
