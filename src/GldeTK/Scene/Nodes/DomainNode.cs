using GldeTK.Scene.Expr;
using System.Collections.Generic;

namespace GldeTK.Scene.Nodes;

/// <summary>
/// translate V | repeat V | rotate V | scale A, children are united
/// </summary>
public sealed class DomainNode : SceneNode
{
    public DomainKind Kind;
    public Vec3Expr V;
    public ExprBase A;
    public List<SceneNode> Children = [];
}
