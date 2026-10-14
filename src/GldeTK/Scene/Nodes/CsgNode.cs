using GldeTK.Scene.Expr;
using System.Collections.Generic;

namespace GldeTK.Scene.Nodes;

public sealed class CsgNode : SceneNode
{
    public CsgKind Kind;
    public ExprBase K; // smooth only
    public List<SceneNode> Children = [];
}
