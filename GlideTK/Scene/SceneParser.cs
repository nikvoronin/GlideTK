using GlideTK.Scene.Defs;
using GlideTK.Scene.Expr;
using GlideTK.Scene.Nodes;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace GlideTK.Scene;

/// <summary>
/// Recursive descent parser of the scene language:
///
///   scene  := (var | directive | node)*
///   var    := 'var' name '=' expr         a named number or vector, visible to the end of its block
///   directive := a name of SceneDefs.Directives and its args: 'sun' vec, 'fog' vec arg, 'camera' vec vec, ...
///   node   := prim args ['color' vec] ['reflect' arg] | block args '{' (var | node)+ '}'
///   args   := arg*                        as declared in <see cref="SceneDefs"/>: a number or a vector each
///   vec    := '(' expr ',' expr ',' expr ')'
///   arg    := number | 't' | 'pi' | name | func '(' expr, ... ')' | vec | '(' expr ')' | '-' arg | arg '.' comp
///   comp   := 'x' | 'y' | 'z' | 'r' | 'g' | 'b'
///   expr   := term (('+' | '-') term)*
///   term   := arg (('*' | '/') arg)*
///
/// "repeat vec 'limit' vec" is the limited repetition.
///
/// Arguments of a node are separated by spaces, so a compound arithmetic argument
/// must be parenthesized: "sphere (1 + 0.2 * sin(t))". Inside a vec any expression is allowed.
///
/// A value is a number or a vector. Number op number is a number; vector op vector works by components;
/// "*" and "/" also take a vector and a number; "+" and "-" do not mix them.
/// An element takes what it declares: a number where a number is declared, a vector for a vector.
/// </summary>
public class SceneParser
{
    static readonly Dictionary<string, (int arity, Func<float[], float> func)> functions =
        new()
        {
            ["sin"] = (1, a => MathF.Sin(a[0])),
            ["cos"] = (1, a => MathF.Cos(a[0])),
            ["abs"] = (1, a => MathF.Abs(a[0])),
            ["min"] = (2, a => MathF.Min(a[0], a[1])),
            ["max"] = (2, a => MathF.Max(a[0], a[1])),
        };

    readonly List<Token> tokens;
    int pos;

    // variables of the file and of the open blocks, the innermost last
    readonly List<Dictionary<string, object>> scopes = [[]];

    SceneParser(string source)
    {
        tokens = SceneLexer.Tokenize(source);
    }

    public static List<SceneNode> Parse(string source)
    {
        var parser = new SceneParser(source);
        var nodes = new List<SceneNode>();
        while (parser.Peek.Kind != TokenKind.End)
        {
            if (parser.IsVar)
            {
                parser.ParseVar();
            }
            else
            {
                nodes.Add(parser.ParseTopLevelNode());
            }
        }

        return nodes;
    }

    Token Peek => tokens[pos];

    Token Next() => tokens[pos++];

    private SceneParseException Error(Token token, string message) =>
        new(message, token.Line, token.Column);

    Token Expect(TokenKind kind, string what)
    {
        Token token = Next();

        return token.Kind == kind ? token 
            : throw Error(token, $"Expected {what}, found {token}");
    }

    // Nodes ----------------------------------------------------------------------

    bool IsVar => Peek.Kind == TokenKind.Ident && Peek.Text == "var";

    static bool IsReserved(string name) =>
        name 
        is "t" 
        or "pi" 
        or "var" 
        or "color" 
        or "reflect" 
        or "limit"
        || functions.ContainsKey(name)
        || vectorFunctions.ContainsKey(name)
        || SceneDefs.Directives.ContainsKey(name)
        || SceneDefs.Primitives.ContainsKey(name)
        || SceneDefs.Blocks.ContainsKey(name);

    /// <returns>VarExpr, VecVarExpr or null</returns>
    object FindVar(string name)
    {
        for (int i = scopes.Count - 1; i >= 0; i--)
        {
            if (scopes[i].TryGetValue(name, out object variable))
                return variable;
        }

        return null;
    }

    /// <summary>'var' name '=' expr, defines a variable and gives no node</summary>
    void ParseVar()
    {
        Next();

        Token name = Expect(TokenKind.Ident, "a variable name after 'var'");

        if (IsReserved(name.Text))
            throw Error(name, $"'{name.Text}' is a reserved word");

        if (FindVar(name.Text) != null)
            throw Error(name, $"Variable '{name.Text}' is already defined");

        Expect(TokenKind.Equals, $"'=' after '{name.Text}'");

        // defined after its value is parsed: a variable cannot use itself
        object value = ParseExpr();
        scopes[^1][name.Text] = value is Vec3Expr vector
            ? new VecVarExpr(vector)
            : new VarExpr((ExprBase)value);
    }

    /// <summary>A shape or an environment directive, directives are not allowed in blocks</summary>
    SceneNode ParseTopLevelNode()
    {
        if (Peek.Kind != TokenKind.Ident
            || !SceneDefs.Directives.TryGetValue(Peek.Text, out PrimDef def))
        {
            return ParseNode();
        }

        var node = new EnvNode
        {
            Def = def,
            Line = Next().Line
        };

        ParseArgs(def.Args, node.Scalars, node.Vectors);

        return node;
    }

    SceneNode ParseNode()
    {
        Token token = Expect(TokenKind.Ident, "scene element");

        if (SceneDefs.Primitives.TryGetValue(token.Text, out PrimDef prim))
            return ParsePrim(token, prim);

        if (SceneDefs.Blocks.TryGetValue(token.Text, out BlockDef block))
            return ParseBlockNode(token, block);

        throw Error(token, $"Unknown scene element '{token.Text}'");
    }

    PrimNode ParsePrim(Token token, PrimDef def)
    {
        var node = new PrimNode
        {
            Def = def,
            Line = token.Line
        };

        ParseArgs(def.Args, node.Scalars, node.Vectors);

        if (Peek.Kind == TokenKind.Ident
            && Peek.Text == "color")
        {
            Next();
            node.Color = ParseVectorArg();
        }

        if (Peek.Kind == TokenKind.Ident
            && Peek.Text == "reflect")
        {
            Next();
            node.Reflect = ParseScalarArg();
        }

        return node;
    }

    BlockNode ParseBlockNode(Token token, BlockDef def)
    {
        var node = new BlockNode
        {
            Def = def,
            Line = token.Line
        };

        ParseArgs(def.Args, node.Scalars, node.Vectors);

        if (def.PushOp == SceneOpcodes.REPEAT
            && Peek.Kind == TokenKind.Ident
            && Peek.Text == "limit")
        {
            Next();
            node.Def = SceneDefs.RepeatLimited;
            node.Vectors.Add(ParseVectorArg());
        }

        ParseBlock(token, node.Children);

        return node;
    }

    void ParseArgs(
        ArgKind[] kinds,
        List<ExprBase> scalars,
        List<Vec3Expr> vectors)
    {
        foreach (ArgKind kind in kinds)
        {
            if (kind == ArgKind.Vector)
                vectors.Add(ParseVectorArg());
            else
                scalars.Add(ParseScalarArg());
        }
    }

    void ParseBlock(Token owner, List<SceneNode> children)
    {
        Expect(TokenKind.LBrace, $"'{{' after '{owner.Text}'");

        scopes.Add([]);

        while (Peek.Kind != TokenKind.RBrace)
        {
            if (Peek.Kind == TokenKind.End)
            {
                throw Error(Peek, $"Unclosed block of '{owner.Text}'");
            }

            if (IsVar)
            {
                ParseVar();
            }
            else
            {
                children.Add(ParseNode());
            }
        }

        scopes.RemoveAt(scopes.Count - 1);
        
        Next();

        if (children.Count == 0)
        {
            throw Error(owner, $"Empty block of '{owner.Text}'");
        }
    }

    // Expressions ----------------------------------------------------------------------
    //
    // A value is a number (ExprBase) or a vector (Vec3Expr), the parser passes them as object.

    static readonly Dictionary<string, int> vectorFunctions =
        new()
        {
            ["length"] = 1,
            ["normalize"] = 1,
            ["dot"] = 2,
            ["cross"] = 2,
            ["mix"] = 3,
        };

    ExprBase AsScalar(object value, Token at) =>
        value as ExprBase
        ?? throw Error(at, "Expected a number, found a vector");

    Vec3Expr AsVector(object value, Token at) =>
        value as Vec3Expr
        ?? throw Error(at, "Expected a vector (x, y, z), found a number");

    /// <summary>An argument that must be a number</summary>
    ExprBase ParseScalarArg()
    {
        Token at = Peek;

        return AsScalar(ParseArg(), at);
    }

    /// <summary>An argument that must be a vector: (x, y, z), a variable, (a + b), ...</summary>
    Vec3Expr ParseVectorArg()
    {
        Token at = Peek;

        return AsVector(ParseArg(), at);
    }

    object ParseExpr()
    {
        object left = ParseTerm();

        while (Peek.Kind == TokenKind.Plus
            || Peek.Kind == TokenKind.Minus)
        {
            Token op = Next();
            left = Combine(op, left, ParseTerm());
        }

        return left;
    }

    object ParseTerm()
    {
        object left = ParseArg();

        while (Peek.Kind == TokenKind.Star
            || Peek.Kind == TokenKind.Slash)
        {
            Token op = Next();
            left = Combine(op, left, ParseArg());
        }

        return left;
    }

    /// <summary>
    /// number op number, vector op vector (by components),
    /// '*' and '/' also vector op number and number op vector
    /// </summary>
    object Combine(Token op, object a, object b)
    {
        char c = op.Text[0];

        if (a is ExprBase x && b is ExprBase y)
            return new BinaryExpr(c, x, y);

        if ((c == '+' || c == '-')
            && (a is ExprBase || b is ExprBase))
        {
            throw Error(op, $"Cannot {(c == '+' ? "add" : "subtract")} a number and a vector");
        }

        Vec3Expr left = a as Vec3Expr ?? new VecSplatExpr((ExprBase)a);
        Vec3Expr right = b as Vec3Expr ?? new VecSplatExpr((ExprBase)b);

        Func<Vector3[], Vector3> func = c switch
        {
            '+' => v => v[0] + v[1],
            '-' => v => v[0] - v[1],
            '*' => v => new Vector3(v[0].X * v[1].X, v[0].Y * v[1].Y, v[0].Z * v[1].Z),
            _ => v => new Vector3(v[0].X / v[1].X, v[0].Y / v[1].Y, v[0].Z / v[1].Z),
        };

        return new VecFuncExpr(func, left, right);
    }

    /// <summary>arg := primary ('.' component)*</summary>
    object ParseArg()
    {
        object value = ParsePrimary();

        while (Peek.Kind == TokenKind.Dot)
        {
            Next();
            Token name = Expect(TokenKind.Ident, "a component x, y or z after '.'");
            value = Component(value, name);
        }

        return value;
    }

    VecScalarFuncExpr Component(object value, Token name)
    {
        int index = name.Text switch
        {
            "x" or "r" => 0,
            "y" or "g" => 1,
            "z" or "b" => 2,
            _ => throw Error(name, $"Unknown component '{name.Text}', use x, y, z (or r, g, b)"),
        };

        if (value is not Vec3Expr vector)
            throw Error(name, $"A component '.{name.Text}' needs a vector");

        return new VecScalarFuncExpr(v => v[0][index], vector);
    }

    object ParsePrimary()
    {
        Token token = Next();

        switch (token.Kind)
        {
            case TokenKind.Number:
                return new ConstExpr(token.Number);

            case TokenKind.Minus:
            {
                object operand = ParseArg();

                return operand is Vec3Expr vector
                    ? new VecFuncExpr(v => -v[0], vector)
                    : new NegExpr((ExprBase)operand);
            }

            case TokenKind.LParen:
            {
                Token first = Peek;
                object inner = ParseExpr();

                if (Peek.Kind != TokenKind.Comma)
                {
                    Expect(TokenKind.RParen, "')'");
                    return inner;
                }

                // (x, y, z)
                ExprBase x = AsScalar(inner, first);

                Next();
                Token at = Peek;
                ExprBase y = AsScalar(ParseExpr(), at);

                Expect(TokenKind.Comma, "','");
                at = Peek;
                ExprBase z = AsScalar(ParseExpr(), at);

                Expect(TokenKind.RParen, "')' of a vector");

                return new Vec3LiteralExpr(x, y, z);
            }

            case TokenKind.Ident:
            {
                if (token.Text == "t")
                    return new TimeExpr();

                if (token.Text == "pi")
                    return new ConstExpr(MathF.PI);

                if (functions.ContainsKey(token.Text)
                    || vectorFunctions.ContainsKey(token.Text))
                {
                    return ParseCall(token);
                }

                return FindVar(token.Text)
                    ?? throw Error(token, $"Unknown identifier '{token.Text}'");
            }

            default:
                throw Error(token, $"Expected a number or a vector, found {token}");
        }
    }

    object ParseCall(Token name)
    {
        Expect(TokenKind.LParen, $"'(' after '{name.Text}'");

        var args = new List<object>();
        var at = new List<Token>();
        do
        {
            if (args.Count > 0)
                Next();

            at.Add(Peek);
            args.Add(ParseExpr());
        }
        while (Peek.Kind == TokenKind.Comma);

        Expect(TokenKind.RParen, "')'");

        int arity = functions.TryGetValue(name.Text, out var f)
            ? f.arity
            : vectorFunctions[name.Text];

        if (args.Count != arity)
            throw Error(name, $"'{name.Text}' takes {arity} argument(s), got {args.Count}");

        if (f.func == null)
            return CallVectorFunction(name, args, at);

        var numbers = new ExprBase[args.Count];
        for (int i = 0; i < numbers.Length; i++)
        {
            numbers[i] = args[i] as ExprBase
                ?? throw Error(at[i], $"'{name.Text}' takes numbers");
        }

        return new CallExpr(f.func, numbers);
    }

    /// <summary>length, dot (numbers), normalize, cross (vectors), mix (numbers or vectors)</summary>
    object CallVectorFunction(Token name, List<object> args, List<Token> at)
    {
        Vec3Expr Vector(int i) => AsVector(args[i], at[i]);

        switch (name.Text)
        {
            case "length":
                return new VecScalarFuncExpr(v => v[0].Length, Vector(0));

            case "dot":
                return new VecScalarFuncExpr(v => Vector3.Dot(v[0], v[1]), Vector(0), Vector(1));

            case "normalize":
                // a zero vector stays zero instead of NaN
                return new VecFuncExpr(
                    v => v[0].LengthSquared > 0f ? v[0].Normalized() : Vector3.Zero,
                    Vector(0));

            case "cross":
                return new VecFuncExpr(v => Vector3.Cross(v[0], v[1]), Vector(0), Vector(1));

            default: // mix(a, b, k)
            {
                ExprBase k = AsScalar(args[2], at[2]);

                if (args[0] is ExprBase a && args[1] is ExprBase b)
                    return new CallExpr(v => v[0] + (v[1] - v[0]) * v[2], [a, b, k]);

                return new VecFuncExpr(
                    v => Vector3.Lerp(v[0], v[1], v[2].X),
                    Vector(0),
                    Vector(1),
                    new VecSplatExpr(k));
            }
        }
    }
}
