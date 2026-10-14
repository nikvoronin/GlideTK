using GlideTK.Scene;
using OpenTK.Mathematics;
using System;

namespace GlideTK;

/// <summary>
/// Player physics: a sphere moving through the scene SDF, 
/// the same distance field the GPU draws.
/// </summary>
public class Physics(SceneState scene)
{
    public float GlobalTime = 0;

    const float FREE_FALL_ACCEL = 9.8f;
    const float MAX_FALL_SPEED = 50f;       // keeps a fall step well below the sphere radius
    const float MAX_SUBSTEP = 0.5f;         // of the radius: a sphere cannot pass through a wall in one substep
    const int MAX_PUSH_ITERATIONS = 8;
    const float GROUND_NORMAL_Y = 0.6f;     // contact normals steeper than ~53 degrees are walls

    readonly SceneState scene = scene;
    float fallSpeed;

    /// <summary>True when the last step ended standing on a surface</summary>
    public bool Grounded { get; private set; }

    float Map(Vector3 pos) => SceneVM.Eval(scene.Data, pos);

    const float EPS = 0.001f;
    static readonly Vector3 eps_xyy = new(EPS, 0.0f, 0.0f);
    static readonly Vector3 eps_yxy = new(0.0f, EPS, 0.0f);
    static readonly Vector3 eps_yyx = new(0.0f, 0.0f, EPS);

    /// <summary>
    /// Gradient of the scene distance. On a symmetry axis (the center of a column) the gradient
    /// is zero, the point is then nudged off the axis. Vector3.UnitY where it stays undefined.
    /// </summary>
    public Vector3 GetSurfaceNormal(Vector3 pos)
    {
        for (int i = 0; i < symmetryNudges.Length; i++)
        {
            Vector3 p = pos + symmetryNudges[i];
            var gradient = new Vector3(
                Map(p + eps_xyy) - Map(p - eps_xyy),
                Map(p + eps_yxy) - Map(p - eps_yxy),
                Map(p + eps_yyx) - Map(p - eps_yyx));

            float length = gradient.Length;

            if (length > 1e-6f && float.IsFinite(length))
                return gradient / length;
        }

        return Vector3.UnitY;
    }

    static readonly Vector3[] symmetryNudges =
    [
        Vector3.Zero,
        new(0.0137f, 0.0071f, 0.0113f),
        new(-0.0911f, 0.0533f, 0.0727f),
    ];

    /// <summary>
    /// Moves the player sphere by the input motion plus gravity, 
    /// sliding along the surfaces
    /// </summary>
    /// <param name="center">Sphere center</param>
    /// <param name="motion">Input motion of this step</param>
    /// <param name="delta">Step time, seconds</param>
    /// <param name="radius">Sphere radius</param>
    /// <param name="flying">Gravity is off this step (moving up)</param>
    /// <returns>The new sphere center</returns>
    public Vector3 Step(
        Vector3 center,
        Vector3 motion,
        float delta,
        float radius,
        bool flying)
    {
        fallSpeed = flying
            ? 0f
            : MathF.Min(fallSpeed + FREE_FALL_ACCEL * delta, MAX_FALL_SPEED);

        motion.Y -= fallSpeed * delta;

        bool grounded = false;
        center = MoveSphere(center, motion, radius, ref grounded);

        Grounded = grounded;
        if (grounded)
            fallSpeed = 0f;

        return center;
    }

    /// <summary>
    /// Moves in substeps not longer than half the radius 
    /// and pushes the sphere out of the scene after each
    /// </summary>
    Vector3 MoveSphere(Vector3 center, Vector3 motion, float radius, ref bool grounded)
    {
        float length = motion.Length;
        int substeps = Math.Max(1, (int)MathF.Ceiling(length / (radius * MAX_SUBSTEP)));
        Vector3 substep = float.IsFinite(length) ? motion / substeps : Vector3.Zero;

        for (int i = 0; i < substeps; i++)
        {
            center += substep;
            center = PushOut(center, radius, ref grounded);
        }

        return center;
    }

    /// <summary>
    /// Pushes the sphere out of the scene, away from the closest contact point.
    /// Also frees the player from moving objects and from shapes that appeared on reload.
    /// </summary>
    Vector3 PushOut(Vector3 center, float radius, ref bool grounded)
    {
        for (int i = 0; i < MAX_PUSH_ITERATIONS; i++)
        {
            // scene distances never exceed the true distance:
            // far enough means no contact
            float centerDist = Map(center);
            if (centerDist >= radius || !float.IsFinite(centerDist))
                break;

            Vector3 normal;

            if (centerDist < 0f)
            {
                // the center is inside a shape: leave it by the shortest way out
                // and one radius beyond
                normal = FindExit(center, out float exitDistance);
                center += normal * (exitDistance + radius);
            }
            else
            {
                if (!FindContact(center, radius, out Vector3 contact))
                    break;

                Vector3 toCenter = center - contact;
                float dist = toCenter.Length;
                if (dist >= radius)
                    break;

                // back off to one radius from the contact
                normal = dist > 1e-6f ? toCenter / dist : GetSurfaceNormal(center);
                center += normal * (radius - dist);
            }

            if (normal.Y > GROUND_NORMAL_Y)
                grounded = true;
        }

        return center;
    }

    /// <summary>
    /// The closest point of the scene surface touching the sphere. Distances of subtract, intersect,
    /// smooth and other operations underestimate the true distance, so the center distance alone
    /// reports contacts that do not exist (an open door). The distance sign is exact though:
    /// the sphere touches the scene where its surface points are inside. Those points and the
    /// center are projected onto the surface, the projection closest to the center is the contact.
    /// </summary>
    bool FindContact(Vector3 center, float radius, out Vector3 contact)
    {
        Vector3 closest = default;
        float best = float.PositiveInfinity;

        void Consider(Vector3 from)
        {
            if (ProjectToSurface(from, out Vector3 q))
            {
                float dist = (center - q).LengthSquared;
                if (dist < best)
                {
                    best = dist;
                    closest = q;
                }
            }
        }

        Consider(center);

        // a few of the deepest surface samples
        Span<float> depth = stackalloc float[CONTACT_SAMPLES];
        Span<int> index = stackalloc int[CONTACT_SAMPLES];
        int found = 0;

        for (int i = 0; i < sphereDirections.Length; i++)
        {
            float d = Map(center + sphereDirections[i] * radius);
            if (d >= 0f)
                continue;

            // insertion into the short list of the deepest ones
            int at = found < CONTACT_SAMPLES ? found++ : CONTACT_SAMPLES;
            while (at > 0 && depth[at - 1] > d)
            {
                if (at < CONTACT_SAMPLES)
                {
                    depth[at] = depth[at - 1];
                    index[at] = index[at - 1];
                }
                at--;
            }

            if (at < CONTACT_SAMPLES)
            {
                depth[at] = d;
                index[at] = i;
            }
        }

        for (int i = 0; i < found; i++)
        {
            Consider(center + sphereDirections[index[i]] * radius);
        }

        contact = closest;
        
        return best < float.PositiveInfinity;
    }

    /// <summary>
    /// The shortest of the sampled ways out of the shape containing p. 
    /// The gradient is no help here:
    /// deep inside (on the axis of a column) it points along the medial axis, 
    /// not to the nearest surface.
    /// Inside, -distance is a lower bound of the way out, 
    /// so it is a safe marching step.
    /// </summary>
    Vector3 FindExit(Vector3 p, out float exitDistance)
    {
        Vector3 bestDirection = Vector3.UnitY;
        exitDistance = float.PositiveInfinity;

        foreach (Vector3 direction in sphereDirections)
        {
            float t = 0f;
            for (int i = 0; i < EXIT_MARCH_STEPS && t < exitDistance; i++)
            {
                float d = Map(p + direction * t);
                if (d >= 0f)
                {
                    exitDistance = t;
                    bestDirection = direction;

                    break;
                }

                t += MathF.Max(-d, SURFACE_EPS);
            }
        }

        if (!float.IsFinite(exitDistance))
        {
            exitDistance = 0f;  // no way out found nearby: just move up
        }

        return bestDirection;
    }

    const int EXIT_MARCH_STEPS = 32;

    /// <summary>
    /// Newton steps along the distance gradient onto the surface
    /// </summary>
    bool ProjectToSurface(Vector3 p, out Vector3 q)
    {
        q = p;
        for (int i = 0; i < PROJECT_ITERATIONS; i++)
        {
            float d = Map(q);
            if (MathF.Abs(d) < SURFACE_EPS)
                return true;

            if (!float.IsFinite(d))
                return false;

            q -= GetSurfaceNormal(q) * d;
        }

        return MathF.Abs(Map(q)) < SURFACE_EPS;
    }

    const int CONTACT_SAMPLES = 3;
    const int PROJECT_ITERATIONS = 6;
    const float SURFACE_EPS = 1e-3f;

    /// <summary>
    /// 26 directions to the faces, edges and corners of a cube around the sphere center
    /// </summary>
    static readonly Vector3[] sphereDirections = BuildSphereDirections();

    static Vector3[] BuildSphereDirections()
    {
        var directions = new System.Collections.Generic.List<Vector3>();

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (x != 0 || y != 0 || z != 0)
                    {
                        directions.Add(Vector3.Normalize(new Vector3(x, y, z)));
                    }
                }
            }
        }

        return [.. directions];
    }
}
