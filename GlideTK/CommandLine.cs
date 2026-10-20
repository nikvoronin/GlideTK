using OpenTK.Mathematics;
using System;
using System.Diagnostics;
using System.Globalization;

namespace GlideTK;

/// <summary>
/// GlideTK.exe [scene] [--size WxH]
/// </summary>
public static class CommandLine
{
    public record Options(string Scene, Vector2i Size);

    const string SIZE = "--size";

    /// <summary>
    /// Wrong options do not stop the start: 
    /// they are reported and the defaults stay
    /// </summary>
    public static Options Parse(string[] args)
    {
        string scene = null;
        var size = new Vector2i(Const.DISPLAY_XGA_W, Const.DISPLAY_XGA_H);

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg == SIZE 
                || arg.StartsWith(SIZE + "="))
            {
                string value = arg == SIZE
                    ? (i + 1 < args.Length ? args[++i] : null)
                    : arg[(SIZE.Length + 1)..];

                if (TryParseSize(value, out Vector2i parsed))
                {
                    size = parsed;
                }
                else
                {
                    Report(
                        $"{SIZE} expects WxH, " +
                            $"{Const.DISPLAY_MIN}..{Const.DISPLAY_MAX} each: {value}");
                }
            }
            else if (arg.StartsWith('-'))
            {
                Report($"Unknown option {arg}");
            }
            else if (scene == null)
            {
                scene = arg;
            }
            else
            {
                Report($"Extra argument {arg}");
            }
        }

        return new Options(scene ?? Const.SCENE_FILENAME, size);
    }

    static bool TryParseSize(string value, out Vector2i size)
    {
        size = default;

        string[] parts = value?.Split('x', 'X');
        if (parts is not { Length: 2 }
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int w)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int h)
            || w < Const.DISPLAY_MIN || w > Const.DISPLAY_MAX
            || h < Const.DISPLAY_MIN || h > Const.DISPLAY_MAX)
        {
            return false;
        }

        size = new Vector2i(w, h);

        return true;
    }

    static void Report(string message)
    {
        Debug.WriteLine(message);
        Console.Error.WriteLine(message);
    }
}
