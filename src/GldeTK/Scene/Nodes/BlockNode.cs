using GldeTK.Scene.Defs;
using GldeTK.Scene.Expr;
using System.Collections.Generic;

namespace GldeTK.Scene.Nodes;

/// <summary>
/// A domain, post or fold operation over its children, see <see cref="SceneDefs.Blocks"/>
/// </summary>
public sealed class BlockNode : SceneNode
{
    public BlockDef Def;
    public List<ExprBase> Scalars = [];     // Scalar and Angle arguments in order
    public List<Vec3Expr> Vectors = [];     // Vector arguments in order
    public List<SceneNode> Children = [];
}
