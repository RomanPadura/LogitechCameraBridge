namespace LogitechCameraBridge.Wpf.ViewModels;

public sealed class AboutViewModel
{
    public string Version { get; } = $"{typeof(MainViewModel).Assembly.GetName().Version!.ToString(3)}";
}
