using GldeTK.Scene;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GldeTK;

public class MainWindow : GameWindow
{
    readonly Camera camera;
    readonly FpsController motionCtrl;
    readonly SceneState scene;
    readonly Physics physics;
    readonly Render render;

    /// <param name="scenePath">Scene file, relative to the executable or absolute</param>
    public MainWindow(string scenePath)
        : base(
            new GameWindowSettings { UpdateFrequency = Const.PHYSICS_UPDATE_FREQUENCY },
            new NativeWindowSettings
            {
                Title = Const.APP_NAME,
                ClientSize = new Vector2i(Const.DISPLAY_XGA_W, Const.DISPLAY_XGA_H),
                APIVersion = new System.Version(3, 3),
                Profile = ContextProfile.Core,
            })
    {
        VSync = VSyncMode.Adaptive;

        render = new Render();

        camera = new Camera(
            new Vector3(3, 1, 0),
            new Vector3(-1, 0, 0),
            new Vector3(0, 1, 0));

        scene = new SceneState(scenePath);
        physics = new Physics(scene);
        motionCtrl = new FpsController();
    }

    protected override void OnUpdateFrame(FrameEventArgs e)
    {
        base.OnUpdateFrame(e);

        float delta = (float)e.Time;
        physics.GlobalTime += delta;

        // scene is evaluated once per tick, physics and the next rendered frames use the same data
        scene.Update(physics.GlobalTime);

        // update player input (keyboard_wasd+space+shift + mouse-look)
        Ray motionStep = motionCtrl.Update(delta, camera.RayCopy, KeyboardState, MouseState);

        // gravity free fall
        Vector3 freeFallVector = physics.Gravity(
            delta,
            camera.RayCopy,
            Const.PLAYER_HIT_RADIUS,
            motionStep.Origin.Y > 0
            );

        motionStep.Origin += freeFallVector;

        // wall collide
        float sd = physics.CastRay(
            camera.Origin,
            Vector3.NormalizeFast(motionStep.Origin)
            );

        // when hit the wall
        if (sd <= Const.PLAYER_HIT_RADIUS)
        {
            camera.Target = motionStep.Target;    // view only
            // smooth wall sliding
            Vector3 hitPoint = camera.Origin + motionStep.Origin * Const.PLAYER_HIT_RADIUS;
            Vector3 norm = physics.GetSurfaceNormal(hitPoint);
            Vector3 invNorm = -norm;
            invNorm *= (motionStep.Origin * norm).LengthFast;

            motionStep.Origin -= invNorm;
        }

        camera.Translate(motionStep);

        UpdateWindowKeys(KeyboardState);
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        CursorState = CursorState.Grabbed;
        render.Start();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        render.OnResize(FramebufferSize.X, FramebufferSize.Y);
    }

    private void UpdateWindowKeys(KeyboardState keyboard)
    {
        if (keyboard.IsKeyDown(Const.INPUT_KEY_EXIT))
            Close();

        if (keyboard.IsKeyPressed(Const.INPUT_KEY_RELOAD_SCENE))
            scene.Reload();

        if (keyboard.IsKeyPressed(Const.INPUT_KEY_TOGGLE_BVH))
            render.Bvh = !render.Bvh;

        if (keyboard.IsKeyPressed(Const.INPUT_KEY_FULLSCREEN))
        {
            WindowState = WindowState == WindowState.Fullscreen
                ? WindowState.Normal
                : WindowState.Fullscreen;
        }
    }

    double s1_timer = 0;    // smooth fps printing

    protected override void OnRenderFrame(FrameEventArgs e)
    {
        base.OnRenderFrame(e);

        float delta = (float)e.Time;

        if (physics.GlobalTime - s1_timer > 1)
        {
            Title = $"{Const.APP_NAME}, {Const.RELEASE_DATE} — " +
                $"{delta * 1000:0.}ms, " +
                $"{1.0 / delta:0}fps " +
                $"// {camera.Origin.X:0.0} " +
                    $": {camera.Origin.Y:0.0} " +
                    $": {camera.Origin.Z:0.0} "
                + (render.Bvh ? "// BVH on" : "// BVH off")
                + (scene.LastError != null ? $" // {scene.LastError}" : "");

            s1_timer = physics.GlobalTime;
        }

        render.OnFrame(
            physics.GlobalTime, 
            FramebufferSize.X, 
            FramebufferSize.Y, 
            camera, 
            scene);

        SwapBuffers();
    }

    protected override void OnUnload()
    {
        render.Stop();
        base.OnUnload();
    }
}
