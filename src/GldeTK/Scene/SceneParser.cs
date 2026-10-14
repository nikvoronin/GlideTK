using GldeTK.Scene.Expr;
using GldeTK.Scene.Nodes;
using System;
using System.Collections.Generic;

namespace GldeTK.Scene;

/// <summary>
/// Recursive descent parser of the scene language:
///
///   scene  := node*
///   node   := prim | domain '{' node+ '}' | csg '{' node+ '}'
///   prim   := sphere arg | box vec | plane vec arg | cylinder arg arg | torus arg arg   ['color' vec]
///   domain := translate vec | repeat vec | rotate vec | scale arg
///   csg    := union | subtract | intersect | smooth arg
///   vec    := '(' expr ',' expr ',' expr ')'
///   arg    := number | 't' | 'pi' | func '(' expr, ... ')' | '(' expr ')' | '-' arg
///   expr   := term (('+' | '-') term)*
///   term   := arg (('*' | '/') arg)*
///
/// Arguments of a node are separated by spaces, so a compound arithmetic argument
/// must be parenthesized: "sphere (1 + 0.2 * sin(t))". Inside a vec any expression is allowed.
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
            nodes.Add(parser.ParseNode());
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

    SceneNode ParseNode()
    {
        Token token = Expect(TokenKind.Ident, "scene element");

        return token.Text switch
        {
            "sphere" => ParsePrim(token, PrimKind.Sphere, p => p.A = ParseArg()),
            "box" => ParsePrim(token, PrimKind.Box, p => p.V = ParseVec()),
            "plane" => ParsePrim(token, PrimKind.Plane, p => { p.V = ParseVec(); p.A = ParseArg(); }),
            "cylinder" => ParsePrim(token, PrimKind.Cylinder, p => { p.A = ParseArg(); p.B = ParseArg(); }),
            "torus" => ParsePrim(token, PrimKind.Torus, p => { p.A = ParseArg(); p.B = ParseArg(); }),
            "translate" => ParseDomain(token, DomainKind.Translate, d => d.V = ParseVec()),
            "repeat" => ParseDomain(token, DomainKind.Repeat, d => d.V = ParseVec()),
            "rotate" => ParseDomain(token, DomainKind.Rotate, d => d.V = ParseVec()),
            "scale" => ParseDomain(token, DomainKind.Scale, d => d.A = ParseArg()),
            "union" => ParseCsg(token, CsgKind.Union),
            "subtract" => ParseCsg(token, CsgKind.Subtract),
            "intersect" => ParseCsg(token, CsgKind.Intersect),
            "smooth" => ParseCsg(token, CsgKind.Smooth),

            _ => throw Error(token, $"Unknown scene element '{token.Text}'"),
        };
    }

    PrimNode ParsePrim(
        Token token, 
        PrimKind kind, 
        Action<PrimNode> parseArgs)
    {
        var node = new PrimNode 
        { 
            Kind = kind, 
            Line = token.Line 
        };

        parseArgs(node);

        if (Peek.Kind == TokenKind.Ident 
            && Peek.Text == "color")
        {
            Next();
            node.Color = ParseVec();
        }

        return node;
    }

    DomainNode ParseDomain(
        Token token, 
        DomainKind kind, 
        Action<DomainNode> parseArgs)
    {
        var node = new DomainNode 
        { 
            Kind = kind, 
            Line = token.Line 
        };

        parseArgs(node);
        ParseBlock(token, node.Children);
        
        return node;
    }

    CsgNode ParseCsg(Token token, CsgKind kind)
    {
        var node = new CsgNode 
        { 
            Kind = kind, 
            Line = token.Line 
        };

        if (kind == CsgKind.Smooth)
        {
            node.K = ParseArg();
        }

        ParseBlock(token, node.Children);
        
        return node;
    }

    void ParseBlock(Token owner, List<SceneNode> children)
    {
        Expect(TokenKind.LBrace, $"'{{' after '{owner.Text}'");

        while (Peek.Kind != TokenKind.RBrace)
        {
            if (Peek.Kind == TokenKind.End)
            {
                throw Error(Peek, $"Unclosed block of '{owner.Text}'");
            }

            children.Add(ParseNode());
        }
        
        Next();

        if (children.Count == 0)
        {
            throw Error(owner, $"Empty block of '{owner.Text}'");
        }
    }

    // Expressions ----------------------------------------------------------------------

    Vec3Expr ParseVec()
    {
        Expect(TokenKind.LParen, "'(' of a vector");
        ExprBase x = ParseExpr();
        Expect(TokenKind.Comma, "','");
        ExprBase y = ParseExpr();
        Expect(TokenKind.Comma, "','");
        ExprBase z = ParseExpr();
        Expect(TokenKind.RParen, "')' of a vector");

        return new Vec3Expr(x, y, z);
    }

    ExprBase ParseExpr()
    {
        ExprBase left = ParseTerm();

        while (Peek.Kind == TokenKind.Plus 
            || Peek.Kind == TokenKind.Minus)
        {
            char op = Next().Text[0];
            left = new BinaryExpr(op, left, ParseTerm());
        }

        return left;
    }

    ExprBase ParseTerm()
    {
        ExprBase left = ParseArg();
        
        while (Peek.Kind == TokenKind.Star 
            || Peek.Kind == TokenKind.Slash)
        {
            char op = Next().Text[0];
            left = new BinaryExpr(op, left, ParseArg());
        }

        return left;
    }

    ExprBase ParseArg()
    {
        Token token = Next();

        switch (token.Kind)
        {
            case TokenKind.Number:
                return new ConstExpr(token.Number);

            case TokenKind.Minus:
                return new NegExpr(ParseArg());

            case TokenKind.LParen:
                ExprBase inner = ParseExpr();
                Expect(TokenKind.RParen, "')'");
                return inner;

            case TokenKind.Ident:
                if (token.Text == "t")
                    return new TimeExpr();

                if (token.Text == "pi")
                    return new ConstExpr(MathF.PI);

                if (functions.TryGetValue(token.Text, out var f))
                    return ParseCall(token, f.arity, f.func);

                throw Error(token, $"Unknown identifier '{token.Text}'");

            default:
                throw Error(token, $"Expected a number, found {token}");
        }
    }

    ExprBase ParseCall(
        Token name, 
        int arity, 
        Func<float[], float> func)
    {
        Expect(TokenKind.LParen, $"'(' after '{name.Text}'");
        var args = new List<ExprBase> { ParseExpr() };
        
        while (Peek.Kind == TokenKind.Comma)
        {
            Next();
            args.Add(ParseExpr());
        }
        
        Expect(TokenKind.RParen, "')'");

        return args.Count == arity
            ? (ExprBase)new CallExpr(func, [.. args])
            : throw Error(name, $"'{name.Text}' takes {arity} argument(s), got {args.Count}");
    }
}
