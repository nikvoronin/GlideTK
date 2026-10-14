using System;

namespace GldeTK.Scene.Expr;

public sealed class CallExpr(
    Func<float[], float> func, 
    ExprBase[] args) 
    : ExprBase
{
    readonly Func<float[], float> func = func;
    readonly ExprBase[] args = args;

    public override float Eval(float t)
    {
        var values = new float[args.Length];
        for (int i = 0; i < args.Length; i++)
            values[i] = args[i].Eval(t);
        return func(values);
    }
}
