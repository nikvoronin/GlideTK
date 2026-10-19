using GlideTK.Scene.Defs;
using GlideTK.Scene.Expr;
using System.Collections.Generic;

namespace GlideTK.Scene.Nodes;

/// <summary>
/// A primitive, see <see cref="SceneDefs.Primitives"/>
/// </summary>
public sealed class PrimNode : SceneNode
{
    public PrimDef Def;
    public List<ExprBase> Scalars = [];     // Scalar and Angle arguments in order
    public List<Vec3Expr> Vectors = [];     // Vector arguments in order
    public Vec3Expr Color;                  // null -> default material
    public ExprBase Reflect;                // null -> 0, no reflection
}
