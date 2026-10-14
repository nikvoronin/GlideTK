namespace GldeTK.Scene;

/// <summary>
/// Compiled scene program layout. Keep in sync with the interpreter in shaders/fragment.c.
///
/// data[0] = (programEnd, materialBase, 0, 0); instructions occupy [1, programEnd),
/// materials (rgb) occupy [materialBase, data.Length).
///
/// op  name       vec4 layout                                   action
/// 1   SPHERE     (op, r, mat, -)                               push d
/// 2   BOX        (op, -, mat, -) (sx, sy, sz, -)               push d
/// 3   PLANE      (op, dist, mat, -) (nx, ny, nz, -)            push d
/// 4   CYLINDER   (op, r, mat, halfHeight)                      push d
/// 5   TORUS      (op, R, mat, r)                               push d
/// 10  TRANSLATE  (op, x, y, z)                                 push p - t
/// 11  REPEAT     (op, cx, cy, cz)                              push mod(p, c) - c/2 (axis with c = 0 is not repeated)
/// 12  ROTATE     (op, -, -, -) (row0) (row1) (row2)            push inverse(R) * p
/// 13  SCALE      (op, s, -, -)                                 push p / s, scale *= s
/// 14  POPPOS     (op, -, -, -)                                 pop p
/// 20  UNION      (op, -, -, -)                                 pop b, a; push min
/// 21  SUBTRACT   (op, -, -, -)                                 pop b, a; push max(a, -b)
/// 22  INTERSECT  (op, -, -, -)                                 pop b, a; push max
/// 23  SMOOTH     (op, k, -, -)                                 pop b, a; push smooth min
/// </summary>
public static class SceneOpcodes
{
    public const int SPHERE = 1;
    public const int BOX = 2;
    public const int PLANE = 3;
    public const int CYLINDER = 4;
    public const int TORUS = 5;

    public const int TRANSLATE = 10;
    public const int REPEAT = 11;
    public const int ROTATE = 12;
    public const int SCALE = 13;
    public const int POPPOS = 14;

    public const int UNION = 20;
    public const int SUBTRACT = 21;
    public const int INTERSECT = 22;
    public const int SMOOTH = 23;

    /// <summary>Distance returned for an empty scene.</summary>
    public const float EMPTY_DISTANCE = 1e10f;
}
