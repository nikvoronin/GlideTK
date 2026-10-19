using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GlideTK;

public static class Const
{
    public const string APP_NAME = "GlideTK";
    public const string RELEASE_DATE = "14 Oct 2026";

    public const string FRAGMENT_FILENAME = "GlideTK.shaders.fragment.c";
    public const string VERTEX_FILENAME = "GlideTK.shaders.vertex.c";
    public const string TEXT_VERTEX_FILENAME = "GlideTK.shaders.text_vertex.c";
    public const string TEXT_FRAGMENT_FILENAME = "GlideTK.shaders.text_fragment.c";

    public const string SCENE_FILENAME = "scenes/default.scene";

    public const string UBO_SDELEMENTSMAP_BLOCKNAME = "SdElements";
    public const int UBO_SDELEMENTSMAP_BLOCKCOUNT = 1024;  // vec4 count, must match g_map[] in fragment.c
    public const int UBO_SDELEMENTSMAP_BINDING = 1;
    public const string UF_TIMER = "iGlobalTime";
    public const string UF_RESOLUTION = "iResolution";
    public const string UF_RAY_ORIGIN = "ro";
    public const string UF_PROJECTION_MATRIX = "camProj";
    public const string UF_USE_BVH = "useBvh";
    public const string UF_SUN = "sun";
    public const string UF_FOG_COLOR = "fogColor";
    public const string UF_FOG_DENSITY = "fogDensity";
    public const string UF_SKY = "sky";
    public const string UF_LIGHT = "light";
    public const string UF_SHADOWS = "shadows";
    public const string UF_MARCH = "march";
    public const string UF_BOUNCES = "bounces";
    public const string UF_TEXT_SCREEN_SIZE = "screenSize";
    public const string UF_TEXT_ATLAS = "atlas";

    public const int SCENE_STACK_DEPTH = 8;     // nesting limit, the stacks of the CPU interpreter (SceneVM)
    public const int BVH_STACK_DEPTH = 32;      // must match BVH_STACK_DEPTH in fragment.c

    public const float PLAYER_HIT_RADIUS = 1.0f;

    public const double PHYSICS_UPDATE_FREQUENCY = 100; // Hz, every 10 ms
    public const Keys INPUT_KEY_FULLSCREEN = Keys.F11;
    public const Keys INPUT_KEY_EXIT = Keys.Escape;
    public const Keys INPUT_KEY_HELP = Keys.F1;
    public const Keys INPUT_KEY_RELOAD_SCENE = Keys.F5;
    public const Keys INPUT_KEY_TOGGLE_BVH = Keys.F7;
    public const Keys INPUT_KEY_TOGGLE_GRAVITY = Keys.G;

    public const int DISPLAY_XGA_W = 1024;
    public const int DISPLAY_XGA_H = 768;
}
