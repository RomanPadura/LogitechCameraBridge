using HidSharp;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Controllers;

/// <summary>Simulates an available, idle controller without accessing physical HID devices.</summary>
public sealed class ControllerDiscoveryStub(ICameraBridgeLogger logger) : IControllerDiscovery
{
    public HidDevice FindController(int vid, int pid, uint fullUsage)
    {
        logger.LogInfo($"Using controller stub. VID=0x{vid:X4}, PID=0x{pid:X4}, Usage=0x{fullUsage:X8}.");
        return new StubHidDevice(vid, pid, fullUsage);
    }

    private sealed class StubHidDevice(int vid, int pid, uint fullUsage) : HidDevice
    {
        public override int VendorID => vid;
        public override int ProductID => pid;
        public override int ReleaseNumberBcd => 0;
        public override string DevicePath => $"stub://controller/{vid:X4}/{pid:X4}/{fullUsage:X8}";
        public override string GetFileSystemName() => DevicePath;
        public override string GetProductName(GetStringFlags flags) => "Simulated bridge controller";
        public override int GetMaxInputReportLength() => 13;
        public override int GetMaxOutputReportLength() => 0;
        public override int GetMaxFeatureReportLength() => 0;

        // Bypass HidSharp's native access coordination: this is entirely an in-memory device.
        protected override DeviceStream OpenDeviceAndRestrictAccess(OpenConfiguration openConfig)
            => OpenDeviceDirectly(openConfig);

        protected override DeviceStream OpenDeviceDirectly(OpenConfiguration openConfig)
            => new StubHidStream(this);
    }

    private sealed class StubHidStream(HidDevice device) : HidStream(device)
    {
        private readonly object _gate = new();
        private bool _disposed;
        private int _readTimeout;
        private int _writeTimeout;

        public override int ReadTimeout
        {
            get => _readTimeout;
            set => _readTimeout = ValidateTimeout(value);
        }

        public override int WriteTimeout
        {
            get => _writeTimeout;
            set => _writeTimeout = ValidateTimeout(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (offset > buffer.Length || count > buffer.Length - offset)
                throw new ArgumentException("The requested range exceeds the buffer.");

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (count == 0)
                    return 0;

                // An idle controller generates no reports. Match the physical stream timeout
                // so the existing bridge loop can check cancellation without busy-waiting.
                Monitor.Wait(_gate, ReadTimeout);
                ObjectDisposedException.ThrowIf(_disposed, this);
                throw new TimeoutException("The simulated controller has no input reports.");
            }
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void GetFeature(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void SetFeature(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                Monitor.PulseAll(_gate);
            }
            base.Dispose(disposing);
        }

        private static int ValidateTimeout(int value)
        {
            if (value != Timeout.Infinite && value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            return value;
        }
    }
}
