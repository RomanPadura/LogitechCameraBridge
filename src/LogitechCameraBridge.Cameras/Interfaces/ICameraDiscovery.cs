using LogitechCameraBridge.Cameras.Models;

namespace LogitechCameraBridge.Cameras.Interfaces;

public interface ICameraDiscovery
{
    /// <summary>
    /// Lists DirectShow video input devices without opening filters or starting capture.
    /// All enumeration resources are released on the calling thread before returning.
    /// </summary>
    IReadOnlyList<CameraDeviceInfo> FindCameras();

    /// <summary>
    /// Finds a camera by USB identity and creates its DirectShow filter without starting capture.
    /// The caller must release the returned COM object when finished.
    /// </summary>
    object CreateCameraFilter(int vid, int pid, string usbInterface);
}
