using GldeTK.Scene.Expr;

namespace GldeTK.Scene.Nodes;

/// <summary>
/// sphere A | box V | plane V A | cylinder A B | torus A B
/// </summary>
public sealed class PrimNode : SceneNode
{
    public PrimKind Kind;
    public ExprBase A, B;
    public Vec3Expr V;
    public Vec3Expr Color;  // null -> default material
}
