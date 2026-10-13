using Microsoft.Win32;

namespace VolumeKeeper;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VolumeKeeper";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value &&
               string.Equals(value, Command(), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("自動起動の設定を開けませんでした。");
        if (enabled) key.SetValue(ValueName, Command());
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string Command() => $"\"{Application.ExecutablePath}\"";
}
