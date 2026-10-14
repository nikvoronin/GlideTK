using System;

namespace GlideTK.Scene;

public class SceneParseException(
    string message, 
    int line, 
    int column) 
    : Exception($"({line},{column}): {message}")
{
    public readonly int Line = line, Column = column;
}
