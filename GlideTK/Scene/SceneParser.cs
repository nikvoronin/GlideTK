using GlideTK.Scene.Defs;
using GlideTK.Scene.Expr;
using GlideTK.Scene.Nodes;
using System;
using System.Collections.Generic;

namespace GlideTK.Scene;

/// <summary>
/// Recursive descent parser of the scene language:
///
///   scene  := (var | directive | node)*
///   var    := 'var' name '=' expr         a named expression, visible to the end of its block
///   directive := a name of SceneDefs.Directives and its args: 'sun' vec, 'fog' vec arg, 'camera' vec vec, ...
///   node   := prim args ['color' vec] ['reflect' arg] | block args '{' (var | node)+ '}'
///   args   := (arg | vec)*                as declared in <see cref="SceneDefs"/>
///   vec    := '(' expr ',' expr ',' expr ')'
///   arg    := number | 't' | 'pi' | name | func '(' expr, ... ')' | '(' expr ')' | '-' arg
///   expr   := term (('+' | '-') term)*
///   term   := arg (('*' | '/') arg)*
///
/// "repeat vec 'limit' vec" is the limited repetition.
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

    // variables of the file and of the open blocks, the innermost last
    readonly List<Dictionary<string, VarExpr>> scopes = [[]];

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
        || SceneDefs.Directives.ContainsKey(name)
        || SceneDefs.Primitives.ContainsKey(name)
        || SceneDefs.Blocks.ContainsKey(name);

    VarExpr FindVar(string name)
    {
        for (int i = scopes.Count - 1; i >= 0; i--)
        {
            if (scopes[i].TryGetValue(name, out VarExpr variable))
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
        scopes[^1][name.Text] = new VarExpr(ParseExpr());
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
            node.Color = ParseVec();
        }

        if (Peek.Kind == TokenKind.Ident
            && Peek.Text == "reflect")
        {
            Next();
            node.Reflect = ParseArg();
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
            node.Vectors.Add(ParseVec());
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
                vectors.Add(ParseVec());
            else
                scalars.Add(ParseArg());
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
            {
                ExprBase inner = ParseExpr();
                Expect(TokenKind.RParen, "')'");
                return inner;
            }

            case TokenKind.Ident:
            {
                if (token.Text == "t")
                    return new TimeExpr();

                if (token.Text == "pi")
                    return new ConstExpr(MathF.PI);

                if (functions.TryGetValue(token.Text, out var f))
                    return ParseCall(token, f.arity, f.func);

                if (FindVar(token.Text) is VarExpr variable)
                    return variable;

                throw Error(token, $"Unknown identifier '{token.Text}'");
            }

            default:
                throw Error(token, $"Expected a number, found {token}");
        }
    }

    CallExpr ParseCall(
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
            ? new CallExpr(func, [.. args])
            : throw Error(name, $"'{name.Text}' takes {arity} argument(s), got {args.Count}");
    }
}
