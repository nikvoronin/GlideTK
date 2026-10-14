using GldeTK.Scene.Defs;
using GldeTK.Scene.Expr;
using System.Collections.Generic;

namespace GldeTK.Scene.Nodes;

/// <summary>
/// A primitive, see <see cref="SceneDefs.Primitives"/>
/// </summary>
public sealed class PrimNode : SceneNode
{
    public PrimDef Def;
    public List<ExprBase> Scalars = [];     // Scalar and Angle arguments in order
    public List<Vec3Expr> Vectors = [];     // Vector arguments in order
    public Vec3Expr Color;                  // null -> default material
}
