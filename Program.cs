using System;
using System.Windows.Forms;

namespace KeyboardTaskRunner;

static class Program
{
    [STAThread]
    static void Main()
    {
        // applies visual styles, text rendering and the ApplicationHighDpiMode from the .csproj
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
