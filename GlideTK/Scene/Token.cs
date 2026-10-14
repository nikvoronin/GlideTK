namespace GlideTK.Scene;

public struct Token
{
    public TokenKind Kind;
    public string Text;
    public float Number;
    public int Line, Column;

    public override readonly string ToString() => 
        Kind == TokenKind.End ? "end of file" : $"'{Text}'";
}
