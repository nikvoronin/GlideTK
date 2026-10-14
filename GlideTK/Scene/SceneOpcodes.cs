namespace GlideTK.Scene;

/// <summary>
/// Compiled scene program layout. The CPU interprets it (SceneVM), the shader runs the GLSL generated
/// from the instruction sequences (Glsl/SceneGlsl.cs): keep both in sync.
///
/// The scene is a union of leaves, each leaf is a separate stack program:
///   data[0]                              (materialBase, bvhStart, bvhNodeCount, alwaysCount)
///   data[1 .. 1 + alwaysCount)           (start, end, 0, 0) of unbounded leaves, always evaluated
///   leaf programs                        instructions in [start, end)
///   data[bvhStart ..)                    BVH nodes of bounded leaves, 2 vec4 each, root is node 0:
///                                        (min.xyz, left) (max.xyz, right), a leaf node has (min.xyz, -start) (max.xyz, end)
///   data[materialBase ..)                material colors (rgb)
/// A BVH node is skipped when the point is outside its box and the box is not closer than the best
/// distance found so far: the shapes lie inside their boxes (see Bvh/SceneBounds.cs).
///
/// Every instruction is
///   header  (op, length, material, shape)   length counts vec4 including the header, shape is set in the
///                                       first instruction of a leaf: the id of its instruction sequence
///   S       (s0, s1, s2, s3)            scalar arguments, present when the instruction has any arguments
///   V0..Vn  (x, y, z, 0)                vector arguments
/// Angles are passed as (sin, cos) pairs of scalars.
///
/// Opcode ranges:
///   [1, 50)   primitives    push distance * scale (formulas: https://iquilezles.org/articles/distfunctions/)
///   [50, 70)  domain        push transformed position, POPPOS pops it
///   [70, 80)  post          modify the top distance using the current position
///   [80, 100) fold          pop b, a; push a (op) b
/// </summary>
public static class SceneOpcodes
{
    // primitives                           S                       V
    public const int SPHERE = 1;            // r
    public const int BOX = 2;               //                      size
    public const int ROUNDBOX = 3;          // r                    size
    public const int BOXFRAME = 4;          // e                    size
    public const int PLANE = 5;             // dist                 normal
    public const int TORUS = 6;             // R, r
    public const int CAPPEDTORUS = 7;       // sin, cos, R, r
    public const int LINK = 8;              // le, r1, r2
    public const int INFCYLINDER = 9;       // r
    public const int CYLINDER = 10;         // r, h
    public const int SEGCYLINDER = 11;      // r                    a, b
    public const int ROUNDCYLINDER = 12;    // ra, rb, h
    public const int CONE = 13;             // sin, cos, h
    public const int INFCONE = 14;          // sin, cos
    public const int CAPPEDCONE = 15;       // h, r1, r2
    public const int SEGCONE = 16;          // ra, rb               a, b
    public const int SOLIDANGLE = 17;       // sin, cos, r
    public const int CUTSPHERE = 18;        // r, h
    public const int CUTHOLLOWSPHERE = 19;  // r, h, t
    public const int DEATHSTAR = 20;        // ra, rb, d
    public const int ROUNDCONE = 21;        // r1, r2, h
    public const int SEGROUNDCONE = 22;     // r1, r2               a, b
    public const int ELLIPSOID = 23;        //                      radii
    public const int RHOMBUS = 24;          // la, lb, h, ra
    public const int OCTAHEDRON = 25;       // s
    public const int PYRAMID = 26;          // h
    public const int HEXPRISM = 27;         // r, h
    public const int TRIPRISM = 28;         // r, h
    public const int CAPSULE = 29;          // r                    a, b
    public const int VCAPSULE = 30;         // h, r
    public const int TRIANGLE = 31;         //                      a, b, c
    public const int QUAD = 32;             //                      a, b, c, d

    // domain
    public const int DOMAIN_FIRST = 50;
    public const int TRANSLATE = 50;        //                      offset
    public const int REPEAT = 51;           //                      period              mod(p, c) - c/2, axis with c = 0 is not repeated
    public const int REPEAT_LIMITED = 52;   //                      period, limit       p - c * clamp(round(p / c), -l, l)
    public const int ROTATE = 53;           //                      rows of inverse(R)
    public const int SCALE = 54;            // s
    public const int MIRROR = 55;           //                      axes                abs(p) on axes != 0
    public const int ELONGATE = 56;         //                      h                   p - clamp(p, -h, h)
    public const int TWIST = 57;            // k                                        around Y
    public const int BEND = 58;             // k                                        XY plane
    public const int REVOLVE = 59;          // o                                        (length(p.xz) - o, p.y, 0)
    public const int EXTRUDE_SLICE = 60;    //                                          (p.x, p.y, 0)
    public const int POPPOS = 61;

    // post
    public const int POST_FIRST = 70;
    public const int ROUND = 70;            // r
    public const int ONION = 71;            // thickness
    public const int DISPLACE = 72;         // amplitude, frequency
    public const int EXTRUDE = 73;          // h                                        closes EXTRUDE_SLICE along Z

    // fold
    public const int FOLD_FIRST = 80;
    public const int UNION = 80;
    public const int SUBTRACT = 81;         //                                          a minus b
    public const int INTERSECT = 82;
    public const int XOR = 83;
    public const int SMOOTH_UNION = 84;     // k
    public const int SMOOTH_SUBTRACT = 85;  // k
    public const int SMOOTH_INTERSECT = 86; // k

    /// <summary>Distance returned for an empty scene.</summary>
    public const float EMPTY_DISTANCE = 1e10f;
}
