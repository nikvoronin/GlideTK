using System.Collections.Generic;

namespace GlideTK.Scene.Glsl;

/// <summary>
/// GLSL expressions of the instructions, 
/// the generator expands them for every instruction of a shape.
/// Mirror of <see cref="SceneVM"/>, 
/// any change here must be repeated there.
///
/// Tokens: $S the scalar vec4 of the instruction, $V0..$V3 its vector arguments (vec3),
/// $p $s the current position and scale, $d the top distance (post),
/// $a $b the folded distances and $k the smoothing radius (fold).
/// </summary>
static class GlslOps
{
    public static readonly Dictionary<int, string> Primitives = new()
    {
        [SceneOpcodes.SPHERE] = "sdSphere($p, $S.x)",
        [SceneOpcodes.BOX] = "sdBox($p, $V0)",
        [SceneOpcodes.ROUNDBOX] = "sdRoundBox($p, $V0, $S.x)",
        [SceneOpcodes.BOXFRAME] = "sdBoxFrame($p, $V0, $S.x)",
        [SceneOpcodes.PLANE] = "sdPlane($p, $V0, $S.x)",
        [SceneOpcodes.TORUS] = "sdTorus($p, $S.x, $S.y)",
        [SceneOpcodes.CAPPEDTORUS] = "sdCappedTorus($p, $S.xy, $S.z, $S.w)",
        [SceneOpcodes.LINK] = "sdLink($p, $S.x, $S.y, $S.z)",
        [SceneOpcodes.INFCYLINDER] = "sdInfCylinder($p, $S.x)",
        [SceneOpcodes.CYLINDER] = "sdCylinder($p, $S.x, $S.y)",
        [SceneOpcodes.SEGCYLINDER] = "sdSegCylinder($p, $V0, $V1, $S.x)",
        [SceneOpcodes.ROUNDCYLINDER] = "sdRoundCylinder($p, $S.x, $S.y, $S.z)",
        [SceneOpcodes.CONE] = "sdCone($p, $S.xy, $S.z)",
        [SceneOpcodes.INFCONE] = "sdInfCone($p, $S.xy)",
        [SceneOpcodes.CAPPEDCONE] = "sdCappedCone($p, $S.x, $S.y, $S.z)",
        [SceneOpcodes.SEGCONE] = "sdSegCone($p, $V0, $V1, $S.x, $S.y)",
        [SceneOpcodes.SOLIDANGLE] = "sdSolidAngle($p, $S.xy, $S.z)",
        [SceneOpcodes.CUTSPHERE] = "sdCutSphere($p, $S.x, $S.y)",
        [SceneOpcodes.CUTHOLLOWSPHERE] = "sdCutHollowSphere($p, $S.x, $S.y, $S.z)",
        [SceneOpcodes.DEATHSTAR] = "sdDeathStar($p, $S.x, $S.y, $S.z)",
        [SceneOpcodes.ROUNDCONE] = "sdRoundCone($p, $S.x, $S.y, $S.z)",
        [SceneOpcodes.SEGROUNDCONE] = "sdSegRoundCone($p, $V0, $V1, $S.x, $S.y)",
        [SceneOpcodes.ELLIPSOID] = "sdEllipsoid($p, $V0)",
        [SceneOpcodes.RHOMBUS] = "sdRhombus($p, $S.x, $S.y, $S.z, $S.w)",
        [SceneOpcodes.OCTAHEDRON] = "sdOctahedron($p, $S.x)",
        [SceneOpcodes.PYRAMID] = "sdPyramid($p, $S.x)",
        [SceneOpcodes.HEXPRISM] = "sdHexPrism($p, $S.xy)",
        [SceneOpcodes.TRIPRISM] = "sdTriPrism($p, $S.xy)",
        [SceneOpcodes.CAPSULE] = "sdCapsule($p, $V0, $V1, $S.x)",
        [SceneOpcodes.VCAPSULE] = "sdVerticalCapsule($p, $S.x, $S.y)",
        [SceneOpcodes.TRIANGLE] = "udTriangle($p, $V0, $V1, $V2)",
        [SceneOpcodes.QUAD] = "udQuad($p, $V0, $V1, $V2, $V3)",
        [SceneOpcodes.MANDELBULB] = "sdMandelbulb($p, $S.x, $S.y)",
        [SceneOpcodes.MENGERSPONGE] = "sdMengerSponge($p, $S.x, $S.y)",
    };

    const string KEEP_SCALE = "$s";

    /// <summary>The new position and the new scale</summary>
    public static readonly Dictionary<int, (string Position, string Scale)> Domains = new()
    {
        [SceneOpcodes.TRANSLATE] = ("$p - $V0", KEEP_SCALE),
        [SceneOpcodes.REPEAT] = ("opRep($p, $V0)", KEEP_SCALE),
        [SceneOpcodes.REPEAT_LIMITED] = ("opRepLimited($p, $V0, $V1)", KEEP_SCALE),
        [SceneOpcodes.ROTATE] = ("vec3(dot($V0, $p), dot($V1, $p), dot($V2, $p))", KEEP_SCALE),
        [SceneOpcodes.SCALE] = ("$p / $S.x", "$s * $S.x"),
        [SceneOpcodes.MIRROR] = ("mix($p, abs($p), notEqual($V0, vec3(0.0)))", KEEP_SCALE),
        [SceneOpcodes.ELONGATE] = ("$p - clamp($p, -$V0, $V0)", KEEP_SCALE),
        [SceneOpcodes.TWIST] = (
            "vec3(cos($S.x * $p.y) * $p.x - sin($S.x * $p.y) * $p.z, $p.y, " +
                "sin($S.x * $p.y) * $p.x + cos($S.x * $p.y) * $p.z)",
            KEEP_SCALE),
        [SceneOpcodes.BEND] = (
            "vec3(cos($S.x * $p.x) * $p.x - sin($S.x * $p.x) * $p.y, " +
                "sin($S.x * $p.x) * $p.x + cos($S.x * $p.x) * $p.y, $p.z)",
            KEEP_SCALE),
        [SceneOpcodes.REVOLVE] = ("vec3(length($p.xz) - $S.x, $p.y, 0.0)", KEEP_SCALE),
        [SceneOpcodes.EXTRUDE_SLICE] = ("vec3($p.x, $p.y, 0.0)", KEEP_SCALE),
    };

    /// <summary>The new top distance, $p and $s are the position and scale at the instruction</summary>
    public static readonly Dictionary<int, string> Posts = new()
    {
        [SceneOpcodes.ROUND] = "vec2($d.x - $S.x * $s, $d.y)",
        [SceneOpcodes.ONION] = "vec2(abs($d.x) - $S.x * $s, $d.y)",
        [SceneOpcodes.DISPLACE] =
            "vec2($d.x + $S.x * $s * sin($S.y * $p.x) * sin($S.y * $p.y) * sin($S.y * $p.z), $d.y)",
        [SceneOpcodes.EXTRUDE] =
            "vec2(min(max($d.x, (abs($p.z) - $S.x) * $s), 0.0) + " +
                "length(max(vec2($d.x, (abs($p.z) - $S.x) * $s), 0.0)), $d.y)",
    };

    public static readonly Dictionary<int, string> Folds = new()
    {
        [SceneOpcodes.UNION] = "$a.x < $b.x ? $a : $b",
        [SceneOpcodes.SUBTRACT] = "vec2(max($a.x, -$b.x), $a.y)",
        [SceneOpcodes.INTERSECT] = "$a.x > $b.x ? $a : $b",
        [SceneOpcodes.XOR] =
            "vec2(max(min($a.x, $b.x), -max($a.x, $b.x)), $a.x < $b.x ? $a.y : $b.y)",
        [SceneOpcodes.SMOOTH_UNION] = "opSmooth($a, $b, $k)",
        [SceneOpcodes.SMOOTH_SUBTRACT] =
            "vec2(-opSmooth(vec2(-$a.x, $a.y), $b, $k).x, $a.y)",
        [SceneOpcodes.SMOOTH_INTERSECT] =
            "vec2(-opSmooth(vec2(-$a.x, $a.y), vec2(-$b.x, $b.y), $k).x, " +
                "opSmooth(vec2(-$a.x, $a.y), vec2(-$b.x, $b.y), $k).y)",
    };
}
