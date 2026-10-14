using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GldeTK;

public class FpsController
{
    public FpsController() { }

    /// <summary>
    /// 0.1 is slower than 0.9
    /// </summary>
    float mouse_sensitivity = 0.1f;

    /// <summary>
    /// From 1 to 100. 10 by default
    /// </summary>
    public float MouseSensitivity
    {
        get => mouse_sensitivity * 100f;
        set => mouse_sensitivity = value / 100f;
    }

    readonly float motion_Speed = 5f;
    readonly float motion_jumpImpulse = 3f;

    float yaw = 0.0f;
    float pitch = 0.0f;

    public Ray Update(
        float delta, 
        Ray rayOrigin, 
        KeyboardState keyboard, 
        MouseState mouse)
    {
        Ray motionStep = rayOrigin;

        UpdateMouse(mouse, delta, motionStep);
        UpdateKeyboard(keyboard, delta, motionStep);

        return motionStep;
    }

    protected void UpdateKeyboard(
        KeyboardState keyboard, 
        float delta, 
        Ray nextStep)
    {
        nextStep.Origin = Vector3.Zero;

        float deltaStep = motion_Speed * delta;

        if (keyboard.IsKeyDown(Keys.W))
            nextStep.Origin += nextStep.Target * deltaStep;

        if (keyboard.IsKeyDown(Keys.S))
            nextStep.Origin -= nextStep.Target * deltaStep;

        if (keyboard.IsKeyDown(Keys.A))
            nextStep.Origin -= Vector3.Normalize(Vector3.Cross(nextStep.Target, nextStep.Up)) * deltaStep;

        if (keyboard.IsKeyDown(Keys.D))
            nextStep.Origin += Vector3.Normalize(Vector3.Cross(nextStep.Target, nextStep.Up)) * deltaStep;

        nextStep.Origin *= new Vector3(1f, 0f, 1f);

        if (keyboard.IsKeyDown(Keys.LeftShift))
            nextStep.Origin -= nextStep.Up * deltaStep;

        if (keyboard.IsKeyDown(Keys.Space))
            nextStep.Origin += nextStep.Up * deltaStep * motion_jumpImpulse;
    }

    protected void UpdateMouse(
        MouseState mouse, 
        float delta, 
        Ray nextStep)
    {
        float deltaX = mouse.Delta.X;
        float deltaY = -mouse.Delta.Y;

        if ((deltaX == 0) 
            && (deltaY == 0))
        {
            return;
        }

        yaw += deltaX * mouse_sensitivity * delta;
        pitch += deltaY * mouse_sensitivity * delta;

        const float EPS = 0.001f;
        if (pitch >= MathHelper.PiOver2 - EPS)
        {
            pitch = MathHelper.PiOver2 - EPS;
        }
        else
        {
            if (pitch <= -MathHelper.PiOver2 + EPS)
            {
                pitch = -MathHelper.PiOver2 + EPS;
            }
        }

        /// Sets new Front of the Camera. Angles must be in radians.
        nextStep.SetTarget(yaw + MathHelper.Pi, pitch);
    }
}
