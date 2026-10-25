using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GlideTK;

/// <summary>
/// KHR_debug (core since OpenGL 4.3): the driver reports errors and warnings in words.
/// Every message is printed once, the driver repeats the same warning every frame.
/// </summary>
public static class GlDebug
{
    // the driver calls it later, the delegate must not be collected
    static DebugProc callback;

    static readonly HashSet<string> reported = [];

    public static void Enable()
    {
        callback = OnMessage;

        GL.Enable(EnableCap.DebugOutput);
        GL.DebugMessageCallback(callback, IntPtr.Zero);

        // notifications are chatter: buffer and state details
        GL.DebugMessageControl(
            DebugSourceControl.DontCare,
            DebugTypeControl.DontCare,
            DebugSeverityControl.DebugSeverityNotification,
            0,
            (int[])null,
            false);
    }

    static void OnMessage(
        DebugSource source,
        DebugType type,
        int id,
        DebugSeverity severity,
        int length,
        IntPtr message,
        IntPtr userParam)
    {
        string line = $"GL {severity} {type}: {Marshal.PtrToStringAnsi(message, length)}";

        lock (reported)
        {
            if (!reported.Add(line))
                return;
        }

        Debug.WriteLine(line);
        Console.Error.WriteLine(line);
    }
}
