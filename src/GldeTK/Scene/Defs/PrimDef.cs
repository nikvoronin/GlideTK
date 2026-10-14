namespace GldeTK.Scene.Defs;

/// <summary>
/// A primitive of the scene language: "name arg arg ... [color (r, g, b)]"
/// </summary>
public sealed record PrimDef(
    string Name,
    int Opcode,
    ArgKind[] Args);
