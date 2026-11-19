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
  MTA worker and MainViewModel. The tray shell exposes Status/Cameras/Exit; the bridge initializes once at startup.
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

KeyboardSimulator only submits reports to the already-ready startup bridge. It never loads selection, starts, retries or switches a session.

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
Every control HRESULT is checked. Failures are logged: console continues processing; WPF cancels the session and requires restart.
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

## Tray application: one startup attempt

App creates no startup window/taskbar button. NotifyIcon exposes Status, Cameras and Exit.
The shared console JSON is linked into WPF output/publish. UseControllerStub selects stub/physical DI.

StatusViewModel.InitializeAsync is invoked automatically, once per process. It discovers the
controller, initializes CameraMenuViewModel (loads selection and enumerates cameras off the UI thread),
and matches the saved exact device identity. Missing/unavailable choice is not selected; no fallback.
Cameras are listed even when the controller is missing. If both are available, it sets DevicePath,
runs the monitored bridge, and waits for actual readiness before Active: true. Startup sends no
button reports and performs no movement or video capture. It owns session cancellation and shutdown.

There is no lazy keyboard startup, retry, reconnection, live switching or menu-triggered rescan.
Repeated initialization calls return the same startup task. Camera selection only saves a choice
for the next launch, with a restart notification. The running camera/status stays the startup choice.
CameraMenuViewModel latches scan/save failures. Initialization/runtime faults make Active false,
retain Error...check logs, cancel the worker and require restart. App blocks keys unless Active.

Status rows are read-only ToolStripLabels with red error/no-selection/inactive text. Controller shows
found: Stub / found: Logitech / not found / Error...check logs. ControllerNotFoundException derives
from InvalidOperationException. Camera shows found: <name> / not selected / Error...check logs.
Status is updated by startup and worker progress, without continuous hotplug monitoring.

MonitoredBridgeSession/BridgeActivity retain actual readiness and observed worker failures.
Optional-axis initialization warnings do not invalidate working Zoom. MainViewModel remains an
unused foundation/example in this host; App no longer resolves it. HID/COM work and disposal stay
on BridgeSession's dedicated MTA worker. Exit awaits startup/session and pending settings writes,
flushes logger writes, removes the hook/icon and disposes services.

JsonCameraSelectionStore persists Name/DevicePath under SelectedCamera in
`%LocalAppData%\LogitechCameraBridge\userSettings.json`. Unknown keys are preserved, invalid JSON
is not overwritten, and replacement follows a complete temporary-file write. Camera enumeration
returns managed identity snapshots and releases native resources on its enumeration thread.
The console retains its independent synchronous lifecycle and USB selection by default.

Pending: visual/hardware acceptance of automatic startup/exit and keyboard behavior, About/Update,
and prior physical control checks. The earlier Computer Use inventory did not expose a targetable
notification area, so no visual tray acceptance is claimed.

Latest automated checks: build without warnings/errors; 31 connector/logger/stub and 52 WPF tests
passed (83 total). Tests now cover automatic startup without keys, a single initialization attempt,
unavailable selection, missing controller, next-start selection persistence, restart-only failures,
inactive keyboard suppression and shutdown cancellation. Existing simulated camera, HID stub,
thread cleanup, DI, JSON and key-chord checks pass. Hardware/visual acceptance remains unverified.
Build/test required execution outside sandbox restrictions.

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
