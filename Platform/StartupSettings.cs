using Microsoft.Win32;
namespace MouseClicker.Platform;
public static class StartupSettings
{
    private const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Enabled { get {using var key=Registry.CurrentUser.OpenSubKey(KeyPath);return key?.GetValue("MouseClicker") is string;} }
    public static void SetEnabled(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(KeyPath);
        if(enabled){string path=Application.ExecutablePath;if(!File.Exists(path))throw new FileNotFoundException("找不到启动程序。");key.SetValue("MouseClicker",$"\"{path}\"");}
        else key.DeleteValue("MouseClicker",false);
    }
}
