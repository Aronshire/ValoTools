using System.Windows;
using System.Runtime.InteropServices;
using ValoTools.Services;

namespace ValoTools;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    protected override void OnStartup(StartupEventArgs e)
    {
        DebugConsole.Initialize(e.Args.Contains("--debug", StringComparer.OrdinalIgnoreCase), AllocConsole);
        base.OnStartup(e);
    }
}
