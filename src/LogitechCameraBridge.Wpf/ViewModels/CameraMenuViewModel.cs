using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogitechCameraBridge.Cameras.Interfaces;
using LogitechCameraBridge.Cameras.Models;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Wpf.Services;

namespace LogitechCameraBridge.Wpf.ViewModels;

public partial class CameraMenuViewModel(
    ICameraDiscovery discovery,
    ICameraSelectionStore selectionStore,
    ICameraBridgeLogger logger) : ObservableObject
{
    private Task _operation = Task.CompletedTask;
    private bool _isClosing;
    private bool _scanned;
    private bool _failed;

    [ObservableProperty]
    private IReadOnlyList<CameraDeviceInfo> _cameras = [];

    [ObservableProperty]
    private CameraDeviceInfo? _selectedCamera;

    [ObservableProperty]
    private string _error = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private bool _isBusy;

    private bool CanInteract() => !IsBusy && !_isClosing && !_failed;

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private Task RefreshAsync()
    {
        if (_scanned || !CanInteract())
            return Task.CompletedTask;
        _scanned = true;
        IsBusy = true;
        _operation = RefreshCoreAsync();
        return _operation;
    }

    private async Task RefreshCoreAsync()
    {
        Error = string.Empty;
        try
        {
            SelectedCamera = await selectionStore.LoadAsync();
            // Only managed identity snapshots cross back to the UI thread.
            Cameras = await Task.Run(discovery.FindCameras);
        }
        catch (Exception ex)
        {
            _failed = true;
            Cameras = [];
            Error = "Could not load cameras or saved selection";
            logger.LogError(ex.ToString());
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private Task SelectAsync(CameraDeviceInfo? camera)
    {
        if (!CanInteract() || camera is null || !Cameras.Contains(camera))
            return Task.CompletedTask;
        IsBusy = true;
        _operation = SelectCoreAsync(camera);
        return _operation;
    }

    private async Task SelectCoreAsync(CameraDeviceInfo camera)
    {
        Error = string.Empty;
        try
        {
            await selectionStore.SaveAsync(camera);
            SelectedCamera = camera;
        }
        catch (Exception ex)
        {
            _failed = true;
            Error = "Could not save camera selection";
            logger.LogError(ex.ToString());
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ShutdownAsync()
    {
        _isClosing = true;
        RefreshCommand.NotifyCanExecuteChanged();
        SelectCommand.NotifyCanExecuteChanged();
        await _operation;
    }
}
