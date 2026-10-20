using OpenTK.Mathematics;
using System;

namespace GlideTK.Scene.Expr;

/// <summary>A vector from vectors: operators, normalize, cross, mix</summary>
public sealed class VecFuncExpr(
    Func<Vector3[], Vector3> func,
    params Vec3Expr[] args)
    : Vec3Expr
{
    readonly Func<Vector3[], Vector3> func = func;
    readonly Vec3Expr[] args = args;

    public override Vector3 Eval(float t)
    {
        var values = new Vector3[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            values[i] = args[i].Eval(t);
        }

        return func(values);
    }
}
