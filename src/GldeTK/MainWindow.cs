using GldeTK.Scene;
using GldeTK.Text;
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
    readonly TextRenderer text;

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
        text = new TextRenderer();

        camera = new Camera(
            new Vector3(3, 1, 0),
            new Vector3(-1, 0, 0),
            new Vector3(0, 1, 0));

        scene = new SceneState(scenePath);
        physics = new Physics(scene);
        motionCtrl = new FpsController();

        Title = $"{Const.APP_NAME} - {scene.FileName}";
    }

    protected override void OnUpdateFrame(FrameEventArgs e)
    {
        base.OnUpdateFrame(e);

        float delta = (float)e.Time;
        physics.GlobalTime += delta;

        // scene is evaluated once per tick,
        // physics and the next rendered frames use the same data
        scene.Update(physics.GlobalTime);

        // update player input (keyboard_wasd+space+shift + mouse-look)
        Ray motionStep = motionCtrl.Update(
            delta, 
            camera.RayCopy, 
            KeyboardState, 
            MouseState);

        // player sphere: gravity, collisions and sliding along the surfaces
        Vector3 center = physics.Step(
            camera.Origin,
            motionStep.Origin,
            delta,
            Const.PLAYER_HIT_RADIUS,
            flying: motionStep.Origin.Y > 0);

        motionStep.Origin = center - camera.Origin;
        camera.Translate(motionStep);

        UpdateWindowKeys(KeyboardState);
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        CursorState = CursorState.Grabbed;
        render.Start();
        text.Start();
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

        if (keyboard.IsKeyPressed(Const.INPUT_KEY_HELP))
            showHelp = !showHelp;

        if (keyboard.IsKeyPressed(Const.INPUT_KEY_TOGGLE_BVH))
            render.Bvh = !render.Bvh;

        if (keyboard.IsKeyPressed(Const.INPUT_KEY_FULLSCREEN))
        {
            WindowState = WindowState == WindowState.Fullscreen
                ? WindowState.Normal
                : WindowState.Fullscreen;
        }
    }

    // frame time averaged over HUD_UPDATE_INTERVAL
    const double HUD_UPDATE_INTERVAL = 0.5;
    double frameTimeSum;
    int frameCount;
    double fps, frameMs;

    bool showHelp;

    const string HELP =
        "WASD   move\n" +
        "Mouse  look\n" +
        "Space  up\n" +
        "Shift  down\n" +
        "F1     this help\n" +
        "F5     reload the scene\n" +
        "F7     BVH on/off\n" +
        "F11    fullscreen\n" +
        "Esc    exit";

    static readonly Color4 HUD_TEXT = new(1f, 1f, 1f, 1f);
    static readonly Color4 HUD_HELP = new(1f, 0.92f, 0.65f, 1f);
    static readonly Color4 HUD_ERROR = new(1f, 0.35f, 0.3f, 1f);
    static readonly Color4 HUD_BACKGROUND = new(0f, 0f, 0f, 0.35f);
    const int HUD_SCALE = 2;
    const float HUD_MARGIN = 10f;

    protected override void OnRenderFrame(FrameEventArgs e)
    {
        base.OnRenderFrame(e);

        frameTimeSum += e.Time;
        frameCount++;
        if (frameTimeSum >= HUD_UPDATE_INTERVAL)
        {
            fps = frameCount / frameTimeSum;
            frameMs = 1000.0 * frameTimeSum / frameCount;
            frameTimeSum = 0;
            frameCount = 0;
        }

        render.OnFrame(
            physics.GlobalTime, 
            FramebufferSize.X, 
            FramebufferSize.Y, 
            camera, 
            scene);

        DrawHud();

        SwapBuffers();
    }

    /// <summary>
    /// Frame statistics, help and scene errors over the picture
    /// </summary>
    void DrawHud()
    {
        Vector3 pos = camera.Origin;
        var stats =
            $"{fps:0} fps  {frameMs:0.0} ms\n" +
            $"{pos.X:0.0} {pos.Y:0.0} {pos.Z:0.0}{(physics.Grounded ? "" : "  in the air")}\n" +
            $"{scene.FileName}  BVH {(render.Bvh ? "on" : "off")}\n" +
            "F1 help";

        var at = new Vector2(HUD_MARGIN, HUD_MARGIN);
        at.Y += text.Print(stats, at, HUD_TEXT, HUD_SCALE, HUD_BACKGROUND).Y + HUD_MARGIN;

        if (showHelp)
        {
            at.Y += text.Print(HELP, at, HUD_HELP, HUD_SCALE, HUD_BACKGROUND).Y + HUD_MARGIN;
        }

        if (scene.LastError != null)
        {
            text.Print(
                scene.LastError,
                at,
                HUD_ERROR,
                HUD_SCALE,
                HUD_BACKGROUND,
                maxWidth: FramebufferSize.X - 2 * HUD_MARGIN);
        }

        text.Draw(FramebufferSize.X, FramebufferSize.Y);
    }

    protected override void OnUnload()
    {
        text.Stop();
        render.Stop();
        base.OnUnload();
    }
}
