using MouseClicker.Storage;
using MouseClicker.UI;

namespace MouseClicker;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Logger.Instance.Shutdown();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
