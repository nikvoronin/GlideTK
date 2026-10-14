using System;

namespace GlideTK;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mainWindow = new MainWindow(
            args.Length > 0 ? args[0] : Const.SCENE_FILENAME);
        mainWindow.Run();
    }
}
