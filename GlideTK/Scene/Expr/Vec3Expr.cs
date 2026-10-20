using OpenTK.Mathematics;

namespace GlideTK.Scene.Expr;

/// <summary>
/// A vector value of the scene language, the number is <see cref="ExprBase"/>
/// </summary>
public abstract class Vec3Expr
{
    public abstract Vector3 Eval(float t);
}
