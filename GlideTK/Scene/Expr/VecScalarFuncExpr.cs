using OpenTK.Mathematics;
using System;

namespace GlideTK.Scene.Expr;

/// <summary>A number from vectors: length, dot, a component</summary>
public sealed class VecScalarFuncExpr(
    Func<Vector3[], float> func,
    params Vec3Expr[] args)
    : ExprBase
{
    readonly Func<Vector3[], float> func = func;
    readonly Vec3Expr[] args = args;

    public override float Eval(float t)
    {
        var values = new Vector3[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            values[i] = args[i].Eval(t);
        }

        return func(values);
    }
}
