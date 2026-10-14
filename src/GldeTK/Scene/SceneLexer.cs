using System;
using System.Collections.Generic;
using System.Globalization;

namespace GldeTK.Scene
{
    public class SceneParseException : Exception
    {
        public readonly int Line, Column;

        public SceneParseException(string message, int line, int column)
            : base($"({line},{column}): {message}")
        {
            Line = line;
            Column = column;
        }
    }

    public enum TokenKind { Number, Ident, LParen, RParen, LBrace, RBrace, Comma, Plus, Minus, Star, Slash, End }

    public struct Token
    {
        public TokenKind Kind;
        public string Text;
        public float Number;
        public int Line, Column;

        public override string ToString() => Kind == TokenKind.End ? "end of file" : $"'{Text}'";
    }

    public static class SceneLexer
    {
        public static List<Token> Tokenize(string source)
        {
            var tokens = new List<Token>();
            int i = 0, line = 1, lineStart = 0;

            while (true)
            {
                // whitespace and # comments
                while (i < source.Length)
                {
                    char c = source[i];
                    if (c == '\n')
                    {
                        i++;
                        line++;
                        lineStart = i;
                    }
                    else if (char.IsWhiteSpace(c))
                        i++;
                    else if (c == '#')
                        while (i < source.Length && source[i] != '\n') i++;
                    else
                        break;
                }

                var token = new Token { Line = line, Column = i - lineStart + 1 };

                if (i >= source.Length)
                {
                    token.Kind = TokenKind.End;
                    token.Text = "";
                    tokens.Add(token);
                    return tokens;
                }

                char ch = source[i];
                int start = i;

                if (char.IsDigit(ch) || (ch == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
                {
                    while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) i++;
                    token.Kind = TokenKind.Number;
                    token.Text = source.Substring(start, i - start);
                    if (!float.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out token.Number))
                        throw new SceneParseException($"Invalid number '{token.Text}'", token.Line, token.Column);
                }
                else if (char.IsLetter(ch) || ch == '_')
                {
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                    token.Kind = TokenKind.Ident;
                    token.Text = source.Substring(start, i - start);
                }
                else
                {
                    i++;
                    token.Text = ch.ToString();
                    switch (ch)
                    {
                        case '(': token.Kind = TokenKind.LParen; break;
                        case ')': token.Kind = TokenKind.RParen; break;
                        case '{': token.Kind = TokenKind.LBrace; break;
                        case '}': token.Kind = TokenKind.RBrace; break;
                        case ',': token.Kind = TokenKind.Comma; break;
                        case '+': token.Kind = TokenKind.Plus; break;
                        case '-': token.Kind = TokenKind.Minus; break;
                        case '*': token.Kind = TokenKind.Star; break;
                        case '/': token.Kind = TokenKind.Slash; break;
                        default:
                            throw new SceneParseException($"Unexpected character '{ch}'", token.Line, token.Column);
                    }
                }

                tokens.Add(token);
            }
        }
    }
}
