using OpenTK.Mathematics;

namespace GlideTK;

public class Camera : Ray
{
    public Matrix3 Projection = Matrix3.Zero;

    public override Vector3 Origin
    {
        set
        {
            base.Origin = value;
            UpdateProjection();
        }
    }

    public override Vector3 Target
    {
        set
        {
            base.Target = value;
            UpdateProjection();
        }
    }

    public override Vector3 Up
    {
        set
        {
            base.Up = value;
            UpdateProjection();
        }
    }

    public Camera() { }

    public Camera(
        Vector3 origin, 
        Vector3 target, 
        Vector3 up) 
        : base(origin, target, up)
    {
        UpdateProjection();
    }

    public virtual Ray RayCopy => new(origin, target, up);

    public override void SetTarget(float yaw, float pitch)
    {
        base.SetTarget(yaw, pitch);
        UpdateProjection();
    }

    /// <summary>
    /// Shift camera's origin to Origin+Step and update target and front
    /// </summary>
    /// <param name="step">Translate shift</param>
    public virtual void Translate(Ray ray)
    {
        origin += ray.Origin;
        Target = ray.Target;

        UpdateProjection();
    }

    protected void UpdateProjection() => 
        Projection = GetProjection(target, up);

    public static Matrix3 GetProjection(Vector3 target, Vector3 up)
    {
        Vector3 cu = Vector3.NormalizeFast(Vector3.Cross(target, up));
        Vector3 cv = Vector3.NormalizeFast(Vector3.Cross(cu, target));

        return new Matrix3(cu, cv, target);
    }
}
