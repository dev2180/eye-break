using System;
using System.Linq;
using System.Windows;

namespace EyeBreakSetup
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var app = new Application();
            if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))) Installer.Uninstall();
            else app.Run(new MainWindow(args.Any(a => a.Equals("/update", StringComparison.OrdinalIgnoreCase))));
        }
    }
}
