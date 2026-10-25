using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GlideTK;

public static class Const
{
    public const string APP_NAME = "GlideTK";
    public const string RELEASE_DATE = "14 Oct 2026";

    public const string TRACE_FILENAME = "GlideTK.shaders.trace.c";
    public const string CULL_FILENAME = "GlideTK.shaders.cull.c";
    public const string TEXT_VERTEX_FILENAME = "GlideTK.shaders.text_vertex.c";
    public const string TEXT_FRAGMENT_FILENAME = "GlideTK.shaders.text_fragment.c";

    // the oldest OpenGL the application runs on, there is no fallback
    public const int GL_VERSION_MAJOR = 4;
    public const int GL_VERSION_MINOR = 3;

    public const string SCENE_FILENAME = "scenes/default.scene";

    public const string UBO_SDELEMENTSMAP_BLOCKNAME = "SdElements";
    public const int UBO_SDELEMENTSMAP_BLOCKCOUNT = 1024;  // vec4 count, must match g_map[] in trace.c and cull.c
    public const int UBO_SDELEMENTSMAP_BINDING = 1;
    public const int TILE_LISTS_BINDING = 2;        // the buffer of cull.c and trace.c
    public const int OUTPUT_IMAGE_BINDING = 0;      // the picture of trace.c

    // The picture is traced in tiles, cull.c lists up to TILE_LIST leaves for the primary rays
    // and as many for the shadow rays of every tile, the shaders have the same numbers
    public const int TILE_SIZE = 8;
    public const int TILE_LIST = 64;
    public const int TILE_STRIDE = 2 * TILE_LIST + 2;   // ints of a tile in the buffer

    // A scene with fewer BVH nodes is traced without tiles: the lists cost more than they save
    public const int TILE_MIN_NODES = 8;
    // Explicit uniform locations: layout(location = N) in the shaders must match.
    // A mat3 takes three locations.
    public const int UF_LOC_RESOLUTION = 0;         // the shaders
    public const int UF_LOC_RAY_ORIGIN = 1;
    public const int UF_LOC_PROJECTION_MATRIX = 2;  // 2..4
    public const int UF_LOC_USE_BVH = 5;
    public const int UF_LOC_SUN = 6;
    public const int UF_LOC_FOG_COLOR = 7;
    public const int UF_LOC_FOG_DENSITY = 8;
    public const int UF_LOC_SKY = 9;
    public const int UF_LOC_LIGHT = 10;
    public const int UF_LOC_SHADOWS = 11;
    public const int UF_LOC_MARCH = 12;
    public const int UF_LOC_BOUNCES = 13;
    public const int UF_LOC_TILES = 14;
    public const int UF_LOC_TEXT_SCREEN_SIZE = 0;   // text_vertex.c

    public const int SCENE_STACK_DEPTH = 8;     // nesting limit, the stacks of the CPU interpreter (SceneVM)
    public const int BVH_STACK_DEPTH = 32;      // must match BVH_STACK_DEPTH in trace.c

    public const float PLAYER_HIT_RADIUS = 1.0f;

    public const double PHYSICS_UPDATE_FREQUENCY = 100; // Hz, every 10 ms
    public const Keys INPUT_KEY_FULLSCREEN = Keys.F11;
    public const Keys INPUT_KEY_EXIT = Keys.Escape;
    public const Keys INPUT_KEY_HELP = Keys.F1;
    public const Keys INPUT_KEY_RELOAD_SCENE = Keys.F5;
    public const Keys INPUT_KEY_TOGGLE_BVH = Keys.F7;
    public const Keys INPUT_KEY_TOGGLE_TILES = Keys.F8;
    public const Keys INPUT_KEY_TOGGLE_GRAVITY = Keys.G;

    public const int DISPLAY_XGA_W = 1024;
    public const int DISPLAY_XGA_H = 768;

    // limits of the --size option
    public const int DISPLAY_MIN = 320;
    public const int DISPLAY_MAX = 16384;
}
