using System;

namespace GldeTK;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mainWindow = new MainWindow();
        mainWindow.Run();
    }
}
