# LogitechCameraBridge

Windows console bridge from Logitech GROUP speakerphone HID buttons to Logitech C930e camera controls.
Targets .NET 10. No video capture is started by the bridge.

## Run

```powershell
dotnet build LogitechCameraBridgeSln.sln
dotnet run --project src/LogitechCameraBridgeConsole
```

Edit `src/LogitechCameraBridgeConsole/appSettings.json` before starting the app.
All button codes are hexadecimal strings in `BridgeControllerDevice`.
Set `Logger.LogToConsole` to `true` to see discovery, ranges, reports, and errors.
Restart the app after configuration changes.

`UseControllerStub` is a root-level JSON option, currently set to `true`. Both hosts register
`ControllerDiscoveryStub` as `IControllerDiscovery` when enabled; `false` or a missing option
registers the physical `ControllerDiscovery`. Set it to `false` before testing GROUP buttons.
The stub simulates a controller without physical HID access. WPF keyboard commands submit button
reports to it; otherwise its reads time out so the cancellation loop can stop normally.
Camera discovery/control is real. The console host does not register simulator hotkeys.

## WPF tray app

`src/LogitechCameraBridge.Wpf` targets `net10.0-windows` and uses CommunityToolkit.Mvvm,
DI and options from the same linked `LogitechCameraBridgeConsole/appSettings.json`.
Run it from the VS Code PowerShell terminal:

```powershell
dotnet run --project src/LogitechCameraBridge.Wpf
```

The app creates no startup window or taskbar button. Find the standard application icon
in the Windows notification area (possibly under **Show hidden icons**). Hover over it to
identify `LogitechCameraBridge`, then right-click for **Status**, **Cameras** and **Exit**.
The app starts a stub bridge on the first simulator shortcut. Camera discovery lists Windows DirectShow video inputs
without opening camera filters or starting video capture.
Exit flushes queued logs, disposes services and removes the tray icon before the process ends.
If configuration startup fails, the tooltip says `LogitechCameraBridge - startup failed`;
Exit remains available and no error window is shown.

Open **Cameras** to refresh the device list asynchronously. Select a camera to save its name and
device path in `%LocalAppData%\LogitechCameraBridge\userSettings.json`. A check mark indicates
the saved choice; identical camera names receive numbered labels. The device path distinguishes
individual cameras. When the saved camera is disconnected, the menu retains a disabled
`(saved; unavailable)` entry instead of silently selecting another camera.
The saved choice is restored when the menu is opened after restarting the app.

User preferences are separate from the shared `appSettings.json`, so rebuilds do not replace the
selection. Writes preserve other JSON settings and replace the file after the new document is
complete. Invalid JSON is reported without overwriting the file. Scan/save errors appear in the menu
and are logged; failed saves also request a tray notification. A failed save retains the previous choice.
Exit waits for an in-progress scan or save before disposing application services.
Automatic bridge startup using this selection when a controller is found is a later step.

### Keyboard simulator

With `UseControllerStub: true`, select a camera from **Cameras**, then use these global shortcuts:

| Shortcut | Action |
| --- | --- |
| Hold Z, press main +/= key or numpad + | Zoom In |
| Hold Z, press main - key or numpad - | Zoom Out |
| Hold P, press Left/Right | Pan Left/Right |
| Hold P, press Up/Down | Tilt Up/Down |

Press the prefix first and keep it held while pressing the action key. These are Windows virtual
Z/P keys (the same key positions with a non-Latin layout). The shifted main + key is supported.
Chords work while another application has focus, only while WPF runs in stub mode. A Windows
low-level keyboard hook recognizes them without logging typed text. Z/P and ordinary keys pass
to the foreground application; recognized action keys, their repeats and releases are consumed.
Use a preview window rather than a text field during testing. Ctrl/Alt/Windows modifiers or holding
both Z and P prevent movement. Injected input is ignored. Hook installation failure requests a tray
error; Exit remains available. Exit removes the hook.

The first command opens the saved camera by its exact device path and waits for camera initialization
and the stub input stream. No camera is selected automatically if the saved camera is unavailable.
After changing **Cameras**, the next command stops/releases the previous session before opening the
new camera. Each press sends one configured HID report through the normal bridge control path.
Key repeat does not generate commands; commands received during connection/switching are dropped.
The stub input queue is bounded and is cleared when the stream closes.
These commands physically control the camera, without starting capture. Open a preview separately
to observe movement; Zoom must be supported, and Pan/Tilt may be unavailable. Use short presses.
Exit removes the keyboard hook, cancels the worker and waits for HID/COM cleanup. Errors go to logger.txt
and command failures request a tray notification. Physical controller startup remains future work.

### Status menu

Opening **Status** refreshes three read-only rows asynchronously:

| Row | Values |
| --- | --- |
| Controller | `found: Stub`, `found: Logitech`, `not found`, or `Error...check logs` |
| Camera | `found: <name>`, `not selected`, `not found`, or `Error...check logs` |
| Active | `true` or `false` |

Missing devices, no selected camera, errors and `Active: false` are red. Checks only enumerate
devices; opening Status does not start the bridge, bind a camera filter or move the camera.
`Camera: found` confirms presence in the OS, not camera-control capabilities.
`Active: true` additionally requires the bridge readiness callback for the currently selected camera,
no reported runtime camera error, and successful controller/camera checks. Before the first simulator
command, Active remains false even when both devices are found. A changed camera selection cannot
inherit Active or an error from the previous camera's session. Initialization/terminal failures and
runtime control errors show `Error...check logs`; unsupported optional axes logged before readiness
do not block Zoom or force the ready camera into an error state. Check `logger.txt` for details.
Status is a snapshot refreshed on opening, not continuous hotplug monitoring.

The foundation includes a dedicated MTA bridge worker, cancellation, overlapping-run protection,
and a ViewModel with Start/Stop commands, readiness status and error reporting.
Running status requires camera initialization and an open HID stream; it is not inferred from
the requested start operation. It is a startup readiness signal, not continuous connection monitoring.
Stop and `ShutdownAsync` wait for the worker to release device resources. Exit calls `ShutdownAsync`
before disposing application services; automatic bridge startup is reserved for a later step.
Do not run the console host and a bridge-enabled WPF host simultaneously during hardware tests.

Manual tray check:

1. Run the command and verify that no window or application taskbar button appears.
2. Open **Cameras** and verify that the available cameras appear. With no cameras, expect **No cameras found**.
3. Select a camera, reopen the menu, and verify its check mark and the `SelectedCamera` JSON entry.
4. Choose **Exit**, restart, and open **Cameras** to verify that the same choice is checked.
5. Disconnect the selected camera and reopen the menu: its saved identity should remain unavailable.
6. Choose **Exit** and confirm that the icon disappears and `dotnet run` returns to the prompt.

Subsequent steps will add physical-controller bridge startup, followed by **About** and **Update**.
Each step is kept separate for user verification and a user-created commit.

## Controller commands

| Button | Configuration | Default code | Action |
| --- | --- | --- | --- |
| Zoom In | ZoomInCode | 0x10 | Zoom +1 driver step |
| Zoom Out | ZoomOutCode | 0x20 | Zoom -1 driver step |
| Left | PanLeftCode | 0x08 | Pan -1 driver step |
| Right | PanRightCode | 0x04 | Pan +1 driver step |
| Up | TiltUpCode | 0x01 | Tilt +1 driver step |
| Down | TiltDownCode | 0x02 | Tilt -1 driver step |

The app queries Min, Max, Step, Default, Current, and manual-control capabilities for Zoom, Pan, and Tilt.
Values are limited to the reported ranges. Commands at a limit do not call `Set` again.
Unavailable Pan/Tilt axes are logged and skipped, so Zoom can continue working.
Driver errors during a command are logged; subsequent commands continue to be processed.
Release (0x00), unknown (0xFF), Far End, presets, and unmapped codes do not change the camera.
There is no application-generated button hold/repeat behavior yet.

## Hardware verification

Zoom In/Out, camera control during an active Zoom conference, and normal volume/microphone operation
have been confirmed by the user. Pan/Tilt hardware support and direction are not yet confirmed.

1. Start the app with logging enabled and inspect the Pan/Tilt capability output.
2. Increase zoom above 100, for example to about 200, and observe the camera preview.
3. Press each arrow briefly. Confirm that Left/Right move in the expected horizontal direction
   and Up/Down move in the expected vertical direction. Record the reported values and visible movement.
4. Repeat at minimum zoom to compare behavior. The app does not adjust zoom automatically.
5. Test both limits on each supported axis and verify Zoom still works afterwards.
6. Repeat during a Zoom conference and check volume/microphone operation.
7. Press Ctrl+C. HID reads use a one-second timeout to check cancellation.

Directions use Right/Up as positive initially; actual C930e image movement must be verified,
including the effect of mirrored previews. If an axis is unavailable, retain the log for investigation.
Zoom Min/Max and clean Ctrl+C shutdown also remain pending hardware checks.

## Automated checks

```powershell
dotnet test LogitechCameraBridgeSln.sln
```

The connector tests use a simulated `IAMCameraControl`; WPF lifecycle tests use simulated sessions
and managers, including tests on a WPF dispatcher. They do not verify physical hardware or window layout.
GitHub Actions builds the solution in Release mode and runs the unit tests on a Windows runner
with .NET 10 for pushes to `main` and for every pull request. You can also start **Build and test** manually
from the repository's **Actions** tab. Each test run uploads TRX results as the `test-results` artifact.
See [PROJECT_CONTEXT.md](PROJECT_CONTEXT.md) for implementation details and test status.

DirectShow API reference: [IAMCameraControl](https://learn.microsoft.com/en-us/windows/win32/api/strmif/nn-strmif-iamcameracontrol).
