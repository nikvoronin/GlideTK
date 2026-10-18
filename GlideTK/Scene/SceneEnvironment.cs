using GlideTK.Scene.Nodes;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace GlideTK.Scene;

/// <summary>
/// Light, fog, the camera and the player set by the directives of a scene file, see
/// <see cref="Defs.SceneDefs.Directives"/>. Without a directive the values are the defaults,
/// which are the values that were hard-coded before the directives existed.
/// </summary>
public sealed record SceneEnvironment
{
    /// <summary>Direction to the sun, any length</summary>
    public Vector3 Sun { get; init; }

    public Vector3 FogColor { get; init; } = new(0.8f, 0.9f, 1.0f);

    public float FogDensity { get; init; } = 0.002f;

    /// <summary>Color of the rays that hit nothing, the fog color when it is not set</summary>
    public Vector3? SkyColor { get; init; }

    public Vector3 Sky => SkyColor ?? FogColor;

    /// <summary>(ambient, specular, shininess, gamma)</summary>
    public Vector4 Light { get; init; } = new(0.2f, 1.2f, 16f, 0.8545f);

    /// <summary>(sharpness, max distance), distance 0 turns the shadows off</summary>
    public Vector2 Shadows { get; init; } = new(8f, 25f);

    /// <summary>(max distance, max steps, max shadow steps)</summary>
    public Vector3 March { get; init; } = new(100f, 100f, 256f);

    /// <summary>Free fall acceleration, 0 starts without gravity</summary>
    public float Gravity { get; init; } = 9.8f;

    /// <summary>(sphere radius, walking speed, climb multiplier)</summary>
    public Vector3 Player { get; init; } = new(Const.PLAYER_HIT_RADIUS, 5f, 3f);

    /// <summary>The start camera, null: the default start</summary>
    public Vector3? CameraPosition { get; init; }

    public Vector3? CameraDirection { get; init; }

    /// <summary>The loops of the shader and the physics stay finite whatever the scene file says</summary>
    const float MAX_LOOP = 2000f;

    /// <summary>The sun walks over the sky with the time</summary>
    static Vector3 DefaultSun(float t) => new(
        MathF.Cos(0.1f * t),
        MathF.Abs(MathF.Sin(0.1f * t)),
        MathF.Cos(0.1f * t) * MathF.Sin(0.1f * t));

    /// <summary>Evaluates the directives of the scene at the time t</summary>
    public static SceneEnvironment Evaluate(IReadOnlyList<SceneNode> nodes, float t)
    {
        var env = new SceneEnvironment { Sun = DefaultSun(t) };

        foreach (SceneNode node in nodes)
        {
            if (node is not EnvNode directive)
                continue;

            Vector3 Vec(int i) => directive.Vectors[i].Eval(t);
            float Num(int i) => directive.Scalars[i].Eval(t);

            env = directive.Def.Name switch
            {
                // a zero vector has no direction: keep the default sun
                "sun" => Vec(0) is { LengthSquared: > 1e-12f } sun ? env with { Sun = sun } : env,
                "fog" => env with { FogColor = Vec(0), FogDensity = MathF.Max(Num(0), 0f) },
                "sky" => env with { SkyColor = Vec(0) },
                "light" => env with { Light = new Vector4(Num(0), Num(1), MathF.Max(Num(2), 0f), env.Light.W) },
                "gamma" => env with { Light = new Vector4(env.Light.Xyz, MathF.Max(Num(0), 0.01f)) },
                "shadows" => env with { Shadows = new Vector2(Num(0), MathF.Max(Num(1), 0f)) },
                "march" => env with
                {
                    March = new Vector3(
                        MathF.Max(Num(0), 1f),
                        Math.Clamp(MathF.Floor(Num(1)), 1f, MAX_LOOP),
                        Math.Clamp(MathF.Floor(Num(2)), 0f, MAX_LOOP)),
                },
                "gravity" => env with { Gravity = MathF.Max(Num(0), 0f) },
                "player" => env with
                {
                    Player = new Vector3(MathF.Max(Num(0), 0.05f), MathF.Max(Num(1), 0f), MathF.Max(Num(2), 0f)),
                },
                "camera" => env with { CameraPosition = Vec(0), CameraDirection = Vec(1) },
                _ => env,
            };
        }

        return env;
    }
}
