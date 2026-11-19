namespace LogitechCameraBridge.Wpf.Services;

internal interface INotificationAreaIcon : IDisposable
{
    event Action<System.Windows.Point?>? MenuRequested;
    void Update(SafeIconHandle icon, string text);
    void ShowMessage(string title, string message, bool error);
    void Activate();
    void ReturnFocus();
}
