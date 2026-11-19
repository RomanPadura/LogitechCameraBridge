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
- Controllers: `IControllerDiscovery`, HID collection discovery and Windows HID capability queries.
- Cameras: `ICameraDiscovery`, DirectShow filter creation by VID/PID/interface.
- Connector: `ICameraBridgeManager`, HID command dispatch and camera control processing.
- Console app: JSON configuration, DI registrations, Ctrl+C handling.
- Connector.Tests: simulated camera-control tests; no physical hardware access.
- Wpf: .NET 10 Windows tray executable with CommunityToolkit.Mvvm, DI/options, a dedicated
  MTA worker and MainViewModel. The current tray shell exposes only Exit and does not start the bridge.
- Wpf.Tests: worker cancellation/cleanup, overlap protection, failure/retry, and ViewModel
  readiness/error/shutdown tests on a WPF dispatcher; no hardware access.

All projects target .NET 10. The bridge is Windows-only and does not start video capture.
The caller releases the COM filter returned by camera discovery. The HID stream is disposed when processing exits.
HID discovery uses `HidD_GetPreparsedData` and `HidP_GetCaps`: HidSharp report descriptor reconstruction
failed on the actual GROUP device and must not be reintroduced for discovery.

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

## Tray application: step 1

The user specified a tray-only application: no startup window and no regular taskbar button.
`App.xaml` uses `OnExplicitShutdown` without a StartupUri. `TrayIcon` uses Windows Forms
`NotifyIcon` with a standard application icon, the LogitechCameraBridge tooltip, and only Exit.
Windows Forms support is enabled for this notification-area component; the host remains WPF.
No Window is constructed. Exit disables repeat requests, awaits ViewModel shutdown and logger
flush, calls WPF Shutdown, and disposes the icon/menu/icon resource, DI provider and configuration.
Configuration failures leave the tray shell available with a startup-failed tooltip and Exit.

The original console JSON remains the single source file, linked into WPF output/publish.
The tray shell deliberately does not start the bridge or access devices in this step.

Planned separate steps (the user will verify and commit each step):

1. Tray shell and Exit (implemented).
2. Bridge startup and Status submenu with exactly one current status: Active, Controller not found,
   Camera not found, or Unknown error. Confirm readiness instead of reporting a requested start as Active.
3. Cameras submenu listing available cameras, selection, and persistence across restarts.
   Inspect/extend discovery for this step; no camera picker is implemented yet.
4. About and Update (update behavior still needs to be specified).

Pending: visual tray/Exit verification, automatic bridge startup, typed UI status classification,
camera discovery/selection persistence, About/Update, and hardware lifecycle verification.
Computer Use inventory did not expose the Windows taskbar/notification area as a targetable window,
so the tray icon/menu/Exit behavior could not be visually verified in this session.

Latest automated verification: solution build succeeded with zero warnings/errors; 24 connector/logger
tests and 6 WPF foundation tests passed. Build/test required execution outside sandbox restrictions.

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
