using HidSharp;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Controllers;

/// <summary>Simulates a controller using explicitly submitted button reports, without physical HID access.</summary>
public sealed class ControllerDiscoveryStub(ICameraBridgeLogger logger) : IControllerDiscovery
{
    private readonly object _streamsGate = new();
    private readonly HashSet<StubHidStream> _streams = [];

    public bool TryPressButton(byte reportId, byte buttonCode)
    {
        byte[] report = new byte[13];
        report[0] = reportId;
        report[1] = buttonCode;
        lock (_streamsGate)
        {
            bool accepted = false;
            foreach (var stream in _streams)
                accepted |= stream.TryEnqueue(report);
            return accepted;
        }
    }

    private StubHidStream OpenStream(HidDevice device)
    {
        var stream = new StubHidStream(device, closed =>
        {
            lock (_streamsGate)
                _streams.Remove(closed);
        });
        lock (_streamsGate)
            _streams.Add(stream);
        return stream;
    }

    public HidDevice FindController(int vid, int pid, uint fullUsage)
    {
        logger.LogInfo($"Using controller stub. VID=0x{vid:X4}, PID=0x{pid:X4}, Usage=0x{fullUsage:X8}.");
        return new StubHidDevice(vid, pid, fullUsage, OpenStream);
    }

    private sealed class StubHidDevice(int vid, int pid, uint fullUsage, Func<HidDevice, StubHidStream> open) : HidDevice
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
            => open(this);
    }

    private sealed class StubHidStream(HidDevice device, Action<StubHidStream> closed) : HidStream(device)
    {
        private readonly object _gate = new();
        private bool _disposed;
        private int _readTimeout;
        private int _writeTimeout;
        private readonly Queue<byte[]> _reports = new();

        public bool TryEnqueue(byte[] report)
        {
            lock (_gate)
            {
                if (_disposed || _reports.Count >= 32)
                    return false;
                _reports.Enqueue(report);
                Monitor.PulseAll(_gate);
                return true;
            }
        }

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

                if (count < 13)
                    throw new ArgumentException("A complete 13-byte report buffer is required.");
                long started = Environment.TickCount64;
                while (_reports.Count == 0 && !_disposed)
                {
                    int remaining = ReadTimeout == Timeout.Infinite ? Timeout.Infinite
                        : (int)Math.Max(0, ReadTimeout - (Environment.TickCount64 - started));
                    if (remaining == 0 || !Monitor.Wait(_gate, remaining))
                        throw new TimeoutException("The simulated controller has no input reports.");
                }
                ObjectDisposedException.ThrowIf(_disposed, this);
                Array.Copy(_reports.Dequeue(), 0, buffer, offset, 13);
                return 13;
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
                _reports.Clear();
                Monitor.PulseAll(_gate);
            }
            closed(this);
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
