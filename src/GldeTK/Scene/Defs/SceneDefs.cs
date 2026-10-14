using System.Collections.Generic;
using System.Linq;

namespace GldeTK.Scene.Defs;

/// <summary>
/// Vocabulary of the scene language. The argument order here is the order in a scene file.
/// </summary>
public static class SceneDefs
{
    const ArgKind S = ArgKind.Scalar;
    const ArgKind V = ArgKind.Vector;
    const ArgKind A = ArgKind.Angle;

    public static readonly IReadOnlyDictionary<string, PrimDef> Primitives =
        new PrimDef[]
        {
            new("sphere",           SceneOpcodes.SPHERE,            [S]),           // r
            new("box",              SceneOpcodes.BOX,               [V]),           // (half size)
            new("roundbox",         SceneOpcodes.ROUNDBOX,          [V, S]),        // (half size) r
            new("boxframe",         SceneOpcodes.BOXFRAME,          [V, S]),        // (half size) e
            new("plane",            SceneOpcodes.PLANE,             [V, S]),        // (normal) dist
            new("torus",            SceneOpcodes.TORUS,             [S, S]),        // R r
            new("cappedtorus",      SceneOpcodes.CAPPEDTORUS,       [A, S, S]),     // angle R r
            new("link",             SceneOpcodes.LINK,              [S, S, S]),     // le r1 r2
            new("infcylinder",      SceneOpcodes.INFCYLINDER,       [S]),           // r
            new("cylinder",         SceneOpcodes.CYLINDER,          [S, S]),        // r halfHeight
            new("segcylinder",      SceneOpcodes.SEGCYLINDER,       [V, V, S]),     // (a) (b) r
            new("roundcylinder",    SceneOpcodes.ROUNDCYLINDER,     [S, S, S]),     // r rounding halfHeight
            new("cone",             SceneOpcodes.CONE,              [A, S]),        // angle height
            new("infcone",          SceneOpcodes.INFCONE,           [A]),           // angle
            new("cappedcone",       SceneOpcodes.CAPPEDCONE,        [S, S, S]),     // halfHeight r1 r2
            new("segcone",          SceneOpcodes.SEGCONE,           [V, V, S, S]),  // (a) (b) ra rb
            new("solidangle",       SceneOpcodes.SOLIDANGLE,        [A, S]),        // angle r
            new("cutsphere",        SceneOpcodes.CUTSPHERE,         [S, S]),        // r h
            new("cuthollowsphere",  SceneOpcodes.CUTHOLLOWSPHERE,   [S, S, S]),     // r h thickness
            new("deathstar",        SceneOpcodes.DEATHSTAR,         [S, S, S]),     // ra rb d
            new("roundcone",        SceneOpcodes.ROUNDCONE,         [S, S, S]),     // r1 r2 h
            new("segroundcone",     SceneOpcodes.SEGROUNDCONE,      [V, V, S, S]),  // (a) (b) r1 r2
            new("ellipsoid",        SceneOpcodes.ELLIPSOID,         [V]),           // (radii)
            new("rhombus",          SceneOpcodes.RHOMBUS,           [S, S, S, S]),  // la lb halfHeight rounding
            new("octahedron",       SceneOpcodes.OCTAHEDRON,        [S]),           // s
            new("pyramid",          SceneOpcodes.PYRAMID,           [S]),           // height, base 1x1
            new("hexprism",         SceneOpcodes.HEXPRISM,          [S, S]),        // r halfLength
            new("triprism",         SceneOpcodes.TRIPRISM,          [S, S]),        // r halfLength
            new("capsule",          SceneOpcodes.CAPSULE,           [V, V, S]),     // (a) (b) r
            new("vcapsule",         SceneOpcodes.VCAPSULE,          [S, S]),        // height r
            new("triangle",         SceneOpcodes.TRIANGLE,          [V, V, V]),     // (a) (b) (c)
            new("quad",             SceneOpcodes.QUAD,              [V, V, V, V]),  // (a) (b) (c) (d)
        }
        .ToDictionary(d => d.Name);

    public static readonly IReadOnlyDictionary<string, BlockDef> Blocks =
        new BlockDef[]
        {
            // domain
            new("translate",        [V], ArgTarget.Push, PushOp: SceneOpcodes.TRANSLATE),
            new("repeat",           [V], ArgTarget.Push, PushOp: SceneOpcodes.REPEAT),
            new("rotate",           [V], ArgTarget.Push, PushOp: SceneOpcodes.ROTATE),
            new("scale",            [S], ArgTarget.Push, PushOp: SceneOpcodes.SCALE),
            new("mirror",           [V], ArgTarget.Push, PushOp: SceneOpcodes.MIRROR),
            new("elongate",         [V], ArgTarget.Push, PushOp: SceneOpcodes.ELONGATE),
            new("twist",            [S], ArgTarget.Push, PushOp: SceneOpcodes.TWIST),
            new("bend",             [S], ArgTarget.Push, PushOp: SceneOpcodes.BEND),
            new("revolve",          [S], ArgTarget.Push, PushOp: SceneOpcodes.REVOLVE),
            new("extrude",          [S], ArgTarget.Post, PushOp: SceneOpcodes.EXTRUDE_SLICE, PostOp: SceneOpcodes.EXTRUDE),

            // post
            new("round",            [S],    ArgTarget.Post, PostOp: SceneOpcodes.ROUND),
            new("onion",            [S],    ArgTarget.Post, PostOp: SceneOpcodes.ONION),
            new("displace",         [S, S], ArgTarget.Post, PostOp: SceneOpcodes.DISPLACE),

            // fold
            new("union",            [],  ArgTarget.None, FoldOp: SceneOpcodes.UNION),
            new("subtract",         [],  ArgTarget.None, FoldOp: SceneOpcodes.SUBTRACT),
            new("intersect",        [],  ArgTarget.None, FoldOp: SceneOpcodes.INTERSECT),
            new("xor",              [],  ArgTarget.None, FoldOp: SceneOpcodes.XOR),
            new("smooth",           [S], ArgTarget.Fold, FoldOp: SceneOpcodes.SMOOTH_UNION),
            new("smoothsubtract",   [S], ArgTarget.Fold, FoldOp: SceneOpcodes.SMOOTH_SUBTRACT),
            new("smoothintersect",  [S], ArgTarget.Fold, FoldOp: SceneOpcodes.SMOOTH_INTERSECT),
        }
        .ToDictionary(d => d.Name);

    /// <summary>"repeat (period) limit (count) { }"</summary>
    public static readonly BlockDef RepeatLimited =
        new("repeat", [V, V], ArgTarget.Push, PushOp: SceneOpcodes.REPEAT_LIMITED);
}
