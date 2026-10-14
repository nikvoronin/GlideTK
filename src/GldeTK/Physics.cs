using GldeTK.Scene;
using OpenTK.Mathematics;

namespace GldeTK;

public class Physics
{
    public float GlobalTime = 0;

    readonly SceneState scene;

    public Physics(SceneState scene)
    {
        this.scene = scene;
    }

    // Map projection and raycaster systems ----------------------------------------------------------------------

    float Map(Vector3 pos) => SceneVM.Eval(scene.Data, pos);

    const float EPS = 0.001f;
    Vector3 eps_xyy = new(EPS, 0.0f, 0.0f);
    Vector3 eps_yxy = new(0.0f, EPS, 0.0f);
    Vector3 eps_yyx = new(0.0f, 0.0f, EPS);
    public Vector3 GetSurfaceNormal(Vector3 pos)
    {
        Vector3 n = Vector3.NormalizeFast(
            new Vector3(
                Map(pos + eps_xyy) - Map(pos - eps_xyy),
                Map(pos + eps_yxy) - Map(pos - eps_yxy),
                Map(pos + eps_yyx) - Map(pos - eps_yyx)
                ));

        return n;
    }

    /// <summary>
    /// Marches the ray through the scene
    /// </summary>
    /// <param name="ro">Ray origin</param>
    /// <param name="rd">Ray direction</param>
    /// <returns>Distance travelled along the ray: to the hit surface, or past MAX_DIST / after MAX_RAY_STEPS on a miss</returns>
    public float CastRay(Vector3 ro, Vector3 rd)
    {
        //TODO move to external constants w/ uniq names
        const int MAX_RAY_STEPS = 16;
        const float MIN_DIST = 0.1f;
        float MAX_DIST = 100;

        float t = 0.0f;
        float h = 1.0f;

        for (int i = 0; i < MAX_RAY_STEPS; i++)
        {

            h = Map(ro + rd * t);
            t += h;

            if (h < MIN_DIST || t > MAX_DIST)
                break;
        }

        return t;
    }

    float motion_fallSpeed = .0f;
    const float phys_freeFallAccel = 9.8f;
    public Vector3 Gravity(float delta, Ray rayOrigin, float player_hitRadius, bool stopFallTrick = false)
    {
        if (stopFallTrick)
            motion_fallSpeed = 0f;  // stop fall
        else
            motion_fallSpeed += phys_freeFallAccel * delta; // free fall

        Vector3 fallVector = new(-rayOrigin.Up * motion_fallSpeed * delta);
        float sd = CastRay(rayOrigin.Origin, Vector3.NormalizeFast(fallVector));
        
        // when hit bottom surface
        if (sd <= player_hitRadius)
        {
            motion_fallSpeed = 0f;
            fallVector = Vector3.Zero;
        }

        return fallVector;
    }
}
