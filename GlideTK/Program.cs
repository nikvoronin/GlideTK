using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Runtime.InteropServices;

namespace GlideTK;

static class Program
{
    const uint MB_ICONERROR = 0x10;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    // OpenTK appends a long hint about its error handler to the GLFW message
    static string Reason(GLFWException e) =>
        e.Message.Split(" (this is thrown")[0];

    [STAThread]
    static void Main(string[] args)
    {
        var options = CommandLine.Parse(args);

        MainWindow mainWindow;
        try
        {
            mainWindow = new MainWindow(options.Scene, options.Size);
        }
        catch (GLFWException e)
        {
            // WinExe has no console: the reason is shown in a window
            MessageBoxW(
                IntPtr.Zero,
                $"{Const.APP_NAME} needs OpenGL {Const.GL_VERSION_MAJOR}.{Const.GL_VERSION_MINOR} or newer. " +
                    "Update the video driver or use a GPU that supports it.\n\n" + Reason(e),
                Const.APP_NAME,
                MB_ICONERROR);
            Environment.ExitCode = 1;

            return;
        }

        using (mainWindow)
        {
            mainWindow.Run();
        }
    }
}
