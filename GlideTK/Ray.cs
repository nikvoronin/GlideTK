using OpenTK.Mathematics;
using System;

namespace GlideTK;

public class Ray
{
    protected Vector3 origin = Vector3.Zero;
    protected Vector3 target = Vector3.Zero;
    protected Vector3 up = new(0.0f, 1.0f, 0.0f);

    public virtual Vector3 Origin
    {
        get => origin;
        set => origin = value;
    }

    public virtual Vector3 Target
    {
        get => target;
        set => target = Vector3.NormalizeFast(value);
    }

    public virtual Vector3 Up
    {
        get => up;
        set => up = value;
    }

    public Ray() { }

    public Ray(Vector3 origin, Vector3 target)
    {
        this.origin = origin;
        this.target = target;
    }

    public Ray(Vector3 origin, Vector3 target, Vector3 up)
    {
        this.origin = origin;
        this.target = target;
        this.up = up;
    }

    public virtual void SetTarget(float yaw, float pitch)
    {
        target = Vector3.NormalizeFast(
            new Vector3(
                (float)(Math.Cos(yaw) * Math.Cos(pitch)),
                (float)(Math.Sin(pitch)),
                (float)(Math.Sin(yaw) * Math.Cos(pitch))));
    }

}
