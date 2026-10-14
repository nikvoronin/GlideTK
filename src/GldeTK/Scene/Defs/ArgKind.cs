namespace GldeTK.Scene.Defs;

public enum ArgKind
{
    /// <summary>A number</summary>
    Scalar,

    /// <summary>(x, y, z)</summary>
    Vector,

    /// <summary>A number of degrees, compiled into (sin, cos) scalars</summary>
    Angle,
}
