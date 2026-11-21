namespace LogitechCameraBridge.Wpf.Services;

internal interface IWindowsStartup
{
    bool IsEnabled { get; }
    bool CanEnable { get; }
    void SetEnabled(bool enabled);
}
