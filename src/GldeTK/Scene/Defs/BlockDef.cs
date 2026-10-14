namespace GldeTK.Scene.Defs;

/// <summary>
/// A block of the scene language: "name arg arg ... { children }".
/// Compiles into [PushOp] children folded with FoldOp [POPPOS] [PostOp].
/// </summary>
public sealed record BlockDef(
    string Name,
    ArgKind[] Args,
    ArgTarget ArgsTo,
    int PushOp = 0,
    int FoldOp = SceneOpcodes.UNION,
    int PostOp = 0);
