using GlideTK.Scene.Nodes;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace GlideTK.Scene;

/// <summary>
/// Light, fog and the start camera set by the directives of a scene file (sun, fog, camera).
/// Without a directive the values are the defaults of the renderer.
/// </summary>
/// <param name="Sun">Direction to the sun, any length</param>
/// <param name="FogColor">Also the color of the sky</param>
/// <param name="CameraPosition">null: the default start</param>
public sealed record SceneEnvironment(
    Vector3 Sun,
    Vector3 FogColor,
    float FogDensity,
    Vector3? CameraPosition,
    Vector3? CameraDirection)
{
    /// <summary>The sun walks over the sky with the time</summary>
    static Vector3 DefaultSun(float t) => new(
        MathF.Cos(0.1f * t),
        MathF.Abs(MathF.Sin(0.1f * t)),
        MathF.Cos(0.1f * t) * MathF.Sin(0.1f * t));

    /// <summary>Evaluates the directives of the scene at the time t</summary>
    public static SceneEnvironment Evaluate(IReadOnlyList<SceneNode> nodes, float t)
    {
        var env = new SceneEnvironment(
            DefaultSun(t),
            new Vector3(0.8f, 0.9f, 1.0f),
            0.002f,
            null,
            null);

        foreach (SceneNode node in nodes)
        {
            if (node is not EnvNode directive)
                continue;

            env = directive.Def.Name switch
            {
                // a zero vector has no direction: keep the default sun
                "sun" => directive.Vectors[0].Eval(t) is { LengthSquared: > 1e-12f } sun
                    ? env with { Sun = sun }
                    : env,
                "fog" => env with
                {
                    FogColor = directive.Vectors[0].Eval(t),
                    FogDensity = directive.Scalars[0].Eval(t),
                },
                "camera" => env with
                {
                    CameraPosition = directive.Vectors[0].Eval(t),
                    CameraDirection = directive.Vectors[1].Eval(t),
                },
                _ => env,
            };
        }

        return env;
    }
}
