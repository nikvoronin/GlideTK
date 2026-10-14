using System;

namespace GldeTK.Scene.Expr;

public sealed class BinaryExpr(
    char op, 
    ExprBase left, 
    ExprBase right) 
    : ExprBase
{
    readonly char op = op;
    readonly ExprBase left = left, right = right;

    public override float Eval(float t)
    {
        float a = left.Eval(t);
        float b = right.Eval(t);

        return op switch
        {
            '+' => a + b,
            '-' => a - b,
            '*' => a * b,
            '/' => a / b,

            _ => throw new InvalidOperationException(
                $"Unknown operator '{op}'"),
        };
    }
}
