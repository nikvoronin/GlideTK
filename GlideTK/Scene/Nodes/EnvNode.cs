using GlideTK.Scene.Defs;
using GlideTK.Scene.Expr;
using System.Collections.Generic;

namespace GlideTK.Scene.Nodes;

/// <summary>
/// An environment directive of the file top level, see <see cref="SceneDefs.Directives"/>.
/// It is not a shape, the compiler skips it.
/// </summary>
public sealed class EnvNode : SceneNode
{
    public PrimDef Def;
    public List<ExprBase> Scalars = [];
    public List<Vec3Expr> Vectors = [];
}
