# Project context

## Devices and protocol

- Logitech GROUP speakerphone: VID 0x046D, PID 0x0857, MI_03/COL01.
- Camera-control collection: UsagePage 0xFF00, Usage 0x0001.
- Logitech C930e: VID 0x046D, PID 0x0843, USB interface MI_00.
- Known HID reports are 13 bytes: report ID 0x0A at byte 0, button code at byte 1.
- Zoom In/Out: 0x10/0x20. Left/Right: 0x08/0x04. Up/Down: 0x01/0x02.
- Far End: 0x40, currently ignored. Presets 1/2/3 are outside the project scope.
- Codes 0x00 and 0xFF are always ignored. The user also observed them when toggling the microphone;
  the exact meaning of 0xFF is not established.

## Current structure

- Common: options shared across projects.
- Logger: `ICameraBridgeLogger`, controlled by `Logger.LogToConsole` through `IOptions<LoggerOptions>`.
- Controllers: `IControllerDiscovery`, physical HID discovery and an idle `ControllerDiscoveryStub` implementation.
- Cameras: `ICameraDiscovery`, DirectShow filter creation by VID/PID/interface and camera identity enumeration.
- Connector: `ICameraBridgeManager`, HID command dispatch and camera control processing.
- Console app: JSON configuration, DI registrations, Ctrl+C handling.
- Connector.Tests: simulated camera-control tests; no physical hardware access.
- Wpf: .NET 10 Windows tray executable with CommunityToolkit.Mvvm, DI/options, a dedicated
  MTA worker and MainViewModel. The tray shell exposes Cameras/Exit; keyboard simulation starts the bridge on demand.
- Wpf.Tests: worker cancellation/cleanup, overlap protection, failure/retry, and ViewModel
  readiness/error/shutdown and camera-menu tests on a WPF dispatcher, plus JSON persistence tests; no hardware access.

All projects target .NET 10. The bridge is Windows-only and does not start video capture.
The caller releases the COM filter returned by camera discovery. The HID stream is disposed when processing exits.
HID discovery uses `HidD_GetPreparsedData` and `HidP_GetCaps`: HidSharp report descriptor reconstruction
failed on the actual GROUP device and must not be reintroduced for discovery.

## Controller simulation

The shared JSON now has root-level `"UseControllerStub": true`. WPF and console DI register
`IControllerDiscovery` with `ControllerDiscoveryStub` when true, or `ControllerDiscovery` when
false/missing. Restart after changing it. The stub is a class implementing the existing interface;
there is no separate stub interface and the manager's discovery contract is unchanged.

The stub returns an in-memory HidDevice with the requested identity and a clearly simulated path/name.
Opening it returns an in-memory HidStream: idle reads wait for ReadTimeout then throw TimeoutException,
so the existing bridge loop retains cancellation behavior. Disposal wakes a pending read and the
device can be opened again. No native HID access or descriptor reconstruction is performed.
`TryPressButton(reportId, buttonCode)` queues a 13-byte report for open streams. Each stream has
a bounded 32-report queue, discarded on disposal. Nothing is queued without an open stream.
Camera operations remain real. Physical button tests require false.

### Keyboard simulator

WPF stub DI registers the same ControllerDiscoveryStub singleton for discovery and keyboard input.
`SimulatorHotkeys` uses WH_KEYBOARD_LL, with a rooted callback, only in WPF stub mode.
Hold Z and press main +/= or numpad + to zoom in, Z with main/numpad - to zoom out;
hold P with arrows for Pan/Tilt. Shifted main + is supported. These use Windows virtual key codes.
The callback tracks only relevant key states, ignores injected events, and posts commands to the
WPF dispatcher instead of doing camera/file work inside the hook. No typed text is logged.
`SimulatorKeyChords` suppresses recognized action keys, their repeats and matching releases;
Z/P themselves and ordinary keys pass through to the foreground application. Both prefixes held
or Ctrl/Alt/Windows modifiers prevent movement. Repeats never generate another command.
Prefix state is reconciled before action events to avoid stale state after missed releases.
No window/taskbar button is created. Hook installation errors surface through the tray; Exit removes
the hook before cancelling the worker. Real hook/foreground keyboard behavior still requires manual verification.

`KeyboardSimulator` loads the saved selection on each command. It starts the bridge lazily, waits for
the readiness callback, and submits the configured report ID/button code. Concurrent commands while
starting/switching are dropped. A changed identity cancels/awaits the old worker before updating the
shared options and starting again. Shutdown cancels startup and waits for cleanup; background failures
are observed/logged. No movement occurs at app startup or merely on camera selection.

`CameraToControlOptions.DevicePath` and an exact-path discovery overload allow selected-camera
binding without USB identity assumptions or fallback. The manager bypasses USB configuration when
the path is supplied; console USB selection is preserved otherwise. COM binding/release stays on the
bridge worker. Missing selection/camera and unsupported camera controls are reported, not simulated.

## Pan/Tilt implementation

Button codes are configured in `BridgeControllerDevice`; all six codes must be distinct bytes excluding 0x00/0xFF.
`CameraControls` queries each property through `IAMCameraControl.GetRange` and `Get` without moving it.
Zoom is required. Unavailable/manual-unsupported/invalid Pan or Tilt ranges are logged and skipped.
On each supported button report, read the current property, add or subtract one driver step,
clamp to Min/Max, and call `Set` with `CameraControlFlags.Manual` if the value changes.
Every control HRESULT is checked. Runtime command failures are logged and do not stop other controls.
Right/Up initially increase values; Left/Down decrease them. Physical image directions are pending verification.
No reset, hold/repeat, automatic zoom adjustment, reconnection, or WPF window is implemented yet.

## WPF lifecycle foundation

`ICameraBridgeManager.Start(CancellationToken)` remains available for the console host.
The additional `Start(CancellationToken, Action onReady)` overload invokes the callback on the
calling thread after camera initialization and HID opening, immediately before report processing.
The callback is a startup readiness signal, not continuous device health monitoring.

`BridgeSession` runs this synchronous API on a dedicated MTA thread. Discovery, controls and
HID/COM cleanup stay on that worker. Its task completes after manager cleanup; cancellation
never reports completion before cleanup. A second concurrent run is rejected.
`MainViewModel` marshals progress to its captured UI synchronization context, ignores stale
notifications, prevents overlapping starts, and waits for cancellation during Stop/ShutdownAsync.
`UiCameraBridgeLogger` forwards logging to the existing options-controlled logger and exposes
runtime errors to the ViewModel. The original JSON file is linked into the WPF output.

## Tray application

The user specified a tray-only application: no startup window and no regular taskbar button.
`App.xaml` uses `OnExplicitShutdown` without a StartupUri. `TrayIcon` uses Windows Forms
`NotifyIcon` with a standard application icon, the LogitechCameraBridge tooltip, Cameras and Exit.
Windows Forms support is enabled for this notification-area component; the host remains WPF.
No Window is constructed. Exit disables repeat requests, awaits ViewModel shutdown and logger
flush, calls WPF Shutdown, and disposes the icon/menu/icon resource, DI provider and configuration.
Configuration failures leave the tray shell available with a startup-failed tooltip and Exit.

The original console JSON remains the single source file, linked into WPF output/publish.
The tray shell starts the stub bridge only on a keyboard command. It enumerates cameras when Cameras is opened,
without opening camera filters or starting capture.

### Camera selection

`ICameraDiscovery.FindCameras()` enumerates DirectShow video inputs and returns managed
`CameraDeviceInfo(Name, DevicePath)` records. It releases COM monikers on the enumeration thread.
Devices without a usable identity are omitted; duplicate paths are deduplicated. Discovery remains
independent of the configured VID/PID, so the menu includes other camera models and virtual inputs
registered with DirectShow. Camera-control capability is not probed in this step.

`CameraMenuViewModel` loads the saved choice and scans on a background MTA thread-pool thread
on every Cameras opening. UI stays responsive; overlapping scans/selections are rejected.
Only a successful save changes the checked selection. Missing saved cameras remain displayed as
saved/unavailable, rather than falling back to a different device. Duplicate names have numbered labels.
Exit disables new interactions and awaits in-flight camera operations before flushing logs/disposal.

`JsonCameraSelectionStore` persists `SelectedCamera` with `Name` and `DevicePath` in
`%LocalAppData%\LogitechCameraBridge\userSettings.json`. This per-user state survives builds and is
separate from the linked shared appSettings.json. Reads/writes are serialized, unknown JSON settings
are preserved, and replacement follows a complete temporary-file write in the same directory.
Malformed settings are reported and never silently overwritten. Save failures keep the previous
selection and request a tray error notification; detailed failures go to the logger.
The saved identity is loaded when the menu opens and on keyboard commands for exact camera binding.

Planned separate steps (the user will verify and commit each step):

1. Tray shell and Exit (implemented).
2. Cameras submenu listing available cameras, selection, and JSON persistence across restarts (implemented).
3. Bridge startup when the controller is found, using the previously selected camera, and Status
   submenu with exactly one current status: Active, Controller not found,
   Camera not found, or Unknown error. Confirm readiness instead of reporting a requested start as Active.
4. About and Update (update behavior still needs to be specified).

Pending: visual tray/Cameras/Exit verification, real OS camera enumeration and persistence acceptance,
automatic bridge startup using the saved device identity, typed UI status classification,
About/Update, and hardware lifecycle verification.
Computer Use inventory did not expose the Windows taskbar/notification area as a targetable window,
so the tray icon/menu/Exit behavior could not be visually verified in this session.

Latest automated verification: solution build succeeded with zero warnings/errors; 31 connector/logger/stub
tests and 44 WPF tests passed (75 total). Chord tests cover all action keys, no-repeat behavior,
prefix release ordering, unrelated keys, conflicting modifiers, action-before-prefix and missed prefix release.
Other checks cover all six keyboard command reports,
readiness gating, selection switching/cleanup, cancellation during startup, absent selection,
stub report layout/queue disposal, exact selected-device routing and console USB routing.
Tests also cover controller-stub identity, idle reads, stream
disposal/reopening and true/false/missing-option DI registration, plus background scanning, selection timing,
scan/save failure recovery, shutdown waiting, persistence across store instances, duplicate names,
malformed settings, preservation of unrelated settings, and failed replacement without data loss.
Build/test required execution outside sandbox restrictions. These tests do not establish hardware
or visual acceptance of the camera menu.

## Hardware test status

Confirmed by the user:

- GROUP camera-control collection and C930e are detected.
- Zoom range reported: Min 100, Max 400, Step 1, Default 100, Manual.
- Zoom In/Out visibly changes the image.
- Control works during an active Zoom conference.
- Volume and microphone buttons retain their normal operation.

Pending:

- Pan/Tilt support, reported ranges, visible movement, and correct directions.
- Pan/Tilt behavior at different zoom levels and during an active conference.
- Zoom and Pan/Tilt limits, and clean Ctrl+C shutdown on hardware.

The implementation and simulated tests do not establish hardware acceptance.
