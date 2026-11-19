using System.Runtime.InteropServices;
using DirectShowLib;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Connector.Models;

namespace LogitechCameraBridge.Connector.Tests;

public class CameraControlsTests
{
    [Fact]
    public void InitializationReportsValuesAndRangesWithoutMovingCamera()
    {
        var camera = new FakeCamera();
        CameraControlCapabilities? result = null;
        new CameraControls(camera, new RecordingLogger()).Initialize(value => result = value);
        Assert.NotNull(result);
        Assert.Equal(CameraControlSupport.Supported, result.Zoom);
        Assert.Equal(new CameraControlValue(101, 100, 400), result.ZoomValue);
        Assert.Equal(new CameraControlValue(0, -10, 10), result.PanValue);
        Assert.Equal(new CameraControlValue(0, -10, 10), result.TiltValue);
        Assert.Empty(camera.Sets);
    }

    [Theory]
    [InlineData(unchecked((int)0x80004001), CameraControlSupport.NotSupported)]
    [InlineData(unchecked((int)0x80070490), CameraControlSupport.NotSupported)]
    [InlineData(unchecked((int)0x80070492), CameraControlSupport.NotSupported)]
    [InlineData(unchecked((int)0x80004005), CameraControlSupport.Error)]
    public void UnsupportedPropertyIsDistinctFromDriverFailure(int hresult, CameraControlSupport expected)
    {
        var camera = new FakeCamera();
        camera.States[CameraControlProperty.Pan].RangeResult = hresult;
        CameraControlCapabilities? result = null;
        new CameraControls(camera, new RecordingLogger()).Initialize(value => result = value);
        Assert.Equal(expected, result!.Pan);
        Assert.Null(result.PanValue);
        Assert.Equal(CameraControlSupport.Supported, result.Zoom);
        Assert.Equal(CameraControlSupport.Supported, result.Tilt);
        Assert.Empty(camera.Sets);
    }

    [Fact]
    public void InvalidRangeIsErrorAndAutoOnlyIsNotSupported()
    {
        var camera = new FakeCamera();
        camera.States[CameraControlProperty.Pan].Step = 0;
        camera.States[CameraControlProperty.Tilt].Capabilities = CameraControlFlags.Auto;
        CameraControlCapabilities? result = null;
        new CameraControls(camera, new RecordingLogger()).Initialize(value => result = value);
        Assert.Equal(CameraControlSupport.Error, result!.Pan);
        Assert.Equal(CameraControlSupport.NotSupported, result.Tilt);
    }

    [Fact]
    public void ReportsAllAxesBeforeRequiredZoomFailure()
    {
        var camera = new FakeCamera();
        camera.States[CameraControlProperty.Zoom].RangeResult = FakeCamera.NotImplemented;
        CameraControlCapabilities? result = null;
        var controls = new CameraControls(camera, new RecordingLogger());
        Assert.Throws<COMException>(() => controls.Initialize(value => result = value));
        Assert.Equal(CameraControlSupport.NotSupported, result!.Zoom);
        Assert.Equal(CameraControlSupport.Supported, result.Pan);
        Assert.Equal(CameraControlSupport.Supported, result.Tilt);
        Assert.Empty(camera.Sets);
    }

    [Fact]
    public void MovementPublishesCurrentValuesAndLimitWithoutExtraSet()
    {
        var camera = new FakeCamera();
        CameraControlCapabilities? result = null;
        var controls = new CameraControls(camera, new RecordingLogger());
        controls.Initialize(value => result = value);
        controls.Move(CameraControlProperty.Pan, 1);
        Assert.Equal(new CameraControlValue(3, -10, 10), result!.PanValue);
        Assert.Equal(new CameraControlValue(101, 100, 400), result.ZoomValue);
        camera.States[CameraControlProperty.Pan].Current = 10;
        controls.Move(CameraControlProperty.Pan, 1);
        Assert.Equal(10, result!.PanValue!.Current);
        Assert.Single(camera.Sets);
        camera.States[CameraControlProperty.Pan].GetResult = unchecked((int)0x80004005);
        controls.Move(CameraControlProperty.Pan, -1);
        Assert.Equal(CameraControlSupport.Error, result!.Pan);
        Assert.Null(result.PanValue);
    }

    [Theory]
    [InlineData(CameraControlProperty.Pan, 1, 3)]
    [InlineData(CameraControlProperty.Pan, -1, -3)]
    [InlineData(CameraControlProperty.Tilt, 1, 3)]
    [InlineData(CameraControlProperty.Tilt, -1, -3)]
    public void MovesOneDriverStepInManualMode(CameraControlProperty property, int direction, int expected)
    {
        var camera = new FakeCamera();
        var controls = new CameraControls(camera, new RecordingLogger());
        controls.Initialize();
        Assert.Empty(camera.Sets);

        controls.Move(property, direction);

        Assert.Equal((property, expected, CameraControlFlags.Manual), Assert.Single(camera.Sets));
    }

    [Theory]
    [InlineData(9, 1, 10)]
    [InlineData(-9, -1, -10)]
    public void ClampsAtBothLimits(int current, int direction, int expected)
    {
        var camera = new FakeCamera();
        camera.States[CameraControlProperty.Pan].Current = current;
        var controls = new CameraControls(camera, new RecordingLogger());
        controls.Initialize();

        controls.Move(CameraControlProperty.Pan, direction);

        Assert.Equal(expected, Assert.Single(camera.Sets).Value);
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(-10, -1)]
    public void DoesNotSetWhenAlreadyAtLimit(int current, int direction)
    {
        var camera = new FakeCamera();
        camera.States[CameraControlProperty.Tilt].Current = current;
        var controls = new CameraControls(camera, new RecordingLogger());
        controls.Initialize();

        controls.Move(CameraControlProperty.Tilt, direction);

        Assert.Empty(camera.Sets);
    }

    [Theory]
    [InlineData("GetRange")]
    [InlineData("Get")]
    [InlineData("Manual")]
    [InlineData("Step")]
    [InlineData("Range")]
    public void UnavailablePanDoesNotBlockZoomOrTilt(string failure)
    {
        var camera = new FakeCamera();
        var pan = camera.States[CameraControlProperty.Pan];
        switch (failure)
        {
            case "GetRange": pan.RangeResult = FakeCamera.NotImplemented; break;
            case "Get": pan.GetResult = FakeCamera.NotImplemented; break;
            case "Manual": pan.Capabilities = CameraControlFlags.Auto; break;
            case "Step": pan.Step = 0; break;
            case "Range": pan.Min = pan.Max + 1; break;
        }
        var logger = new RecordingLogger();
        var controls = new CameraControls(camera, logger);
        controls.Initialize();

        controls.Move(CameraControlProperty.Pan, 1);
        controls.Move(CameraControlProperty.Zoom, 1);
        controls.Move(CameraControlProperty.Tilt, -1);

        Assert.Collection(camera.Sets,
            set => Assert.Equal((CameraControlProperty.Zoom, 102, CameraControlFlags.Manual), set),
            set => Assert.Equal((CameraControlProperty.Tilt, -3, CameraControlFlags.Manual), set));
        Assert.Contains(logger.Errors, message => message.Contains("Pan is unavailable"));
    }

    [Theory]
    [InlineData("Get")]
    [InlineData("Set")]
    public void DriverFailureIsLoggedAndOtherControlsKeepWorking(string operation)
    {
        var camera = new FakeCamera();
        var logger = new RecordingLogger();
        var controls = new CameraControls(camera, logger);
        controls.Initialize();
        var pan = camera.States[CameraControlProperty.Pan];
        if (operation == "Get") pan.GetResult = FakeCamera.NotImplemented;
        else pan.SetResult = FakeCamera.NotImplemented;

        controls.Move(CameraControlProperty.Pan, 1);
        controls.Move(CameraControlProperty.Zoom, -1);

        Assert.Contains(logger.Errors, message => message.Contains($"Pan {operation} failed"));
        Assert.Equal((CameraControlProperty.Zoom, 100, CameraControlFlags.Manual), camera.Sets.Last());
        Assert.Equal(0, pan.Current);
    }

    [Fact]
    public void UnsupportedZoomRemainsAStartupError()
    {
        var camera = new FakeCamera();
        camera.States[CameraControlProperty.Zoom].RangeResult = FakeCamera.NotImplemented;
        var controls = new CameraControls(camera, new RecordingLogger());

        var exception = Assert.Throws<COMException>(controls.Initialize);

        Assert.Equal(FakeCamera.NotImplemented, exception.HResult);
    }

    [Fact]
    public void LargeStepDoesNotOverflow()
    {
        var camera = new FakeCamera();
        var pan = camera.States[CameraControlProperty.Pan];
        pan.Min = int.MinValue;
        pan.Max = int.MaxValue;
        pan.Step = int.MaxValue;
        pan.Current = int.MaxValue - 1;
        var controls = new CameraControls(camera, new RecordingLogger());
        controls.Initialize();

        controls.Move(CameraControlProperty.Pan, 1);

        Assert.Equal(int.MaxValue, Assert.Single(camera.Sets).Value);
    }

    private sealed class RecordingLogger : ICameraBridgeLogger
    {
        public List<string> Errors { get; } = [];
        public void LogInfo(string message) { }
        public void LogError(string message) => Errors.Add(message);
    }

    private sealed class State
    {
        public int Min = -10;
        public int Max = 10;
        public int Step = 3;
        public int Current;
        public CameraControlFlags Capabilities = CameraControlFlags.Manual;
        public int RangeResult;
        public int GetResult;
        public int SetResult;
    }

    private sealed class FakeCamera : IAMCameraControl
    {
        public const int NotImplemented = unchecked((int)0x80004001);
        public Dictionary<CameraControlProperty, State> States { get; } = new()
        {
            [CameraControlProperty.Zoom] = new() { Min = 100, Max = 400, Step = 1, Current = 101 },
            [CameraControlProperty.Pan] = new(),
            [CameraControlProperty.Tilt] = new()
        };
        public List<(CameraControlProperty Property, int Value, CameraControlFlags Flags)> Sets { get; } = [];

        public int GetRange(CameraControlProperty property, out int min, out int max,
            out int step, out int defaultValue, out CameraControlFlags flags)
        {
            var state = States[property];
            min = state.Min;
            max = state.Max;
            step = state.Step;
            defaultValue = state.Min;
            flags = state.Capabilities;
            return state.RangeResult;
        }

        public int Get(CameraControlProperty property, out int value, out CameraControlFlags flags)
        {
            var state = States[property];
            value = state.Current;
            flags = state.Capabilities;
            return state.GetResult;
        }

        public int Set(CameraControlProperty property, int value, CameraControlFlags flags)
        {
            var state = States[property];
            Sets.Add((property, value, flags));
            if (state.SetResult >= 0) state.Current = value;
            return state.SetResult;
        }
    }
}
