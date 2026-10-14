using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace GldeTK.Text;

/// <summary>
/// Screen text with the 8x8 bitmap font. Print() queues text during a frame,
/// Draw() renders all of it in one instanced draw call over the frame.
/// </summary>
public sealed class TextRenderer
{
    const int FLOATS_PER_GLYPH = 9;     // x, y, width, height, glyph index, r, g, b, a
    const int LINE_GAP = 2;             // font pixels between lines
    const int BACKGROUND_PADDING = 2;   // font pixels around a line
    static readonly Color4 SHADOW = new(0f, 0f, 0f, 0.85f);

    // drawn in this order: backgrounds under shadows under glyphs
    readonly List<float> backgrounds = [];
    readonly List<float> shadows = [];
    readonly List<float> glyphs = [];

    int h_program,
        h_vao,
        h_vbo,
        h_atlas,
        uf_ScreenSize;

    public void Start()
    {
        h_program = ShaderLoader.LinkProgram(
            Const.TEXT_VERTEX_FILENAME, 
            Const.TEXT_FRAGMENT_FILENAME);
        uf_ScreenSize = GL.GetUniformLocation(
            h_program, 
            Const.UF_TEXT_SCREEN_SIZE);

        GL.UseProgram(h_program);
        GL.Uniform1(GL.GetUniformLocation(h_program, Const.UF_TEXT_ATLAS), 0);     // texture unit 0
        GL.UseProgram(0);

        CreateAtlas();

        const int stride = FLOATS_PER_GLYPH * sizeof(float);
        h_vao = GL.GenVertexArray();
        h_vbo = GL.GenBuffer();
        GL.BindVertexArray(h_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, h_vbo);

        GL.EnableVertexAttribArray(0);     // rect
        GL.VertexAttribPointer(0, 4, VertexAttribPointerType.Float, false, stride, 0);
        GL.VertexAttribDivisor(0, 1);

        GL.EnableVertexAttribArray(1);     // glyph
        GL.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));
        GL.VertexAttribDivisor(1, 1);

        GL.EnableVertexAttribArray(2);     // color
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
        GL.VertexAttribDivisor(2, 1);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    /// <summary>
    /// All glyphs in one row of a single channel texture
    /// </summary>
    void CreateAtlas()
    {
        const int size = Font8x8.SIZE;
        int width = Font8x8.COUNT * size;
        var pixels = new byte[width * size];

        for (int glyph = 0; glyph < Font8x8.COUNT; glyph++)
            for (int row = 0; row < size; row++)
            {
                byte bits = Font8x8.Glyphs[glyph * size + row];
                for (int bit = 0; bit < size; bit++)
                    if ((bits >> bit & 1) != 0)
                        pixels[row * width + glyph * size + bit] = 255;
            }

        h_atlas = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, h_atlas);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.R8,
            width,
            size,
            0,
            PixelFormat.Red,
            PixelType.UnsignedByte,
            pixels);
        GL.TexParameter(
            TextureTarget.Texture2D, 
            TextureParameterName.TextureMinFilter, 
            (int)TextureMinFilter.Nearest);
        GL.TexParameter(
            TextureTarget.Texture2D, 
            TextureParameterName.TextureMagFilter, 
            (int)TextureMagFilter.Nearest);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>
    /// Queues text for this frame. Lines are split by '\n' and wrapped by words at maxWidth,
    /// characters outside ASCII are shown as '?'.
    /// </summary>
    /// <param name="position">Top left corner, pixels from the top left of the window</param>
    /// <param name="scale">Pixels per font pixel, a glyph is 8 * scale pixels</param>
    /// <param name="background">A box under every line, null for none</param>
    /// <param name="maxWidth">Wrap width in pixels, 0 for no wrapping</param>
    /// <returns>Size of the printed block in pixels</returns>
    public Vector2 Print(
        string text,
        Vector2 position,
        Color4 color,
        int scale = 2,
        Color4? background = null,
        float maxWidth = 0f)
    {
        float cell = Font8x8.SIZE * scale;
        float lineHeight = cell + LINE_GAP * scale;
        float padding = BACKGROUND_PADDING * scale;
        int maxColumns = maxWidth > 0f
            ? Math.Max(1, (int)((maxWidth - 2 * padding) / cell))
            : int.MaxValue;

        List<string> lines = Wrap(text, maxColumns);
        int widest = 0;

        for (int line = 0; line < lines.Count; line++)
        {
            string chars = lines[line];
            float y = position.Y + line * lineHeight;

            if (background is Color4 back && chars.Length > 0)
            {
                Add(backgrounds,
                    position.X - padding, y,
                    chars.Length * cell + 2 * padding, lineHeight,
                    Font8x8.BLOCK, back);
            }

            float glyphY = y + LINE_GAP * scale / 2;
            for (int column = 0; column < chars.Length; column++)
            {
                char ch = chars[column];
                if (ch == ' ')
                    continue;

                float x = position.X + column * cell;
                Add(shadows, x + scale, glyphY + scale, cell, cell, ch, SHADOW);
                Add(glyphs, x, glyphY, cell, cell, ch, color);
            }

            widest = Math.Max(widest, chars.Length);
        }

        return new Vector2(widest * cell, lines.Count * lineHeight);
    }

    /// <summary>Splits the text into lines of at most maxColumns, 
    /// breaking at spaces when possible</summary>
    static List<string> Wrap(string text, int maxColumns)
    {
        var lines = new List<string>();

        foreach (string paragraph in text.Split('\n'))
        {
            string rest = paragraph;
            while (rest.Length > maxColumns)
            {
                int split = rest.LastIndexOf(' ', maxColumns);
                if (split <= 0)
                    split = maxColumns;     // a word longer than the line

                lines.Add(rest[..split]);
                rest = rest[split..].TrimStart(' ');
            }

            lines.Add(rest);
        }

        return lines;
    }

    static void Add(
        List<float> list,
        float x,
        float y,
        float width,
        float height,
        char ch,
        Color4 color)
    {
        int glyph = ch >= Font8x8.FIRST && ch < Font8x8.FIRST + Font8x8.COUNT
            ? ch - Font8x8.FIRST
            : '?' - Font8x8.FIRST;

        list.Add(x);
        list.Add(y);
        list.Add(width);
        list.Add(height);
        list.Add(glyph);
        list.Add(color.R);
        list.Add(color.G);
        list.Add(color.B);
        list.Add(color.A);
    }

    /// <summary>
    /// Draws the queued text over the frame and clears the queue
    /// </summary>
    public void Draw(int width, int height)
    {
        int count = (backgrounds.Count + shadows.Count + glyphs.Count) / FLOATS_PER_GLYPH;
        if (count == 0)
            return;

        float[] data = [.. backgrounds, .. shadows, .. glyphs];
        backgrounds.Clear();
        shadows.Clear();
        glyphs.Clear();

        GL.UseProgram(h_program);
        GL.Uniform2(uf_ScreenSize, (float)width, (float)height);

        GL.BindVertexArray(h_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, h_vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StreamDraw);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, h_atlas);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, count);
        GL.Disable(EnableCap.Blend);

        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    public void Stop()
    {
        GL.DeleteBuffer(h_vbo);
        GL.DeleteVertexArray(h_vao);
        GL.DeleteTexture(h_atlas);
        GL.DeleteProgram(h_program);
    }
}
