using System;

namespace GlideTK;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var options = CommandLine.Parse(args);

        using var mainWindow = new MainWindow(options.Scene, options.Size);
        mainWindow.Run();
    }
}
