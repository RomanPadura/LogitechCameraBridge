namespace LogitechCameraBridge.Cameras.Interfaces;

public interface ICameraDiscovery
{
    /// <summary>
    /// Finds a camera by USB identity and creates its DirectShow filter without starting capture.
    /// The caller must release the returned COM object when finished.
    /// </summary>
    object CreateCameraFilter(int vid, int pid, string usbInterface);
}
