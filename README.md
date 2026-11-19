# LogitechCameraBridge

Windows tray and console bridge from Logitech GROUP speakerphone HID buttons to cameras
with driver-exposed Zoom, Pan or Tilt controls through IAMCameraControl.
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

Run from the VS Code PowerShell terminal:

```powershell
dotnet run --project src/LogitechCameraBridge.Wpf
```

The app uses .NET 10 WPF, MVVM, DI and the shared console appSettings.json. It has no startup
window or taskbar button. Its tray menu contains Status, Camera controls, Cameras, About, Restart and Exit.

Startup performs one attempt:

1. Find the configured controller and publish its status.
2. Enumerate cameras and restore the saved choice by its exact device path.
3. If the saved choice is absent/unavailable, show Camera: Not selected without choosing a fallback.
4. If both devices are available, initialize controls and open controller input on the worker.
5. Publish Active: true only after readiness. No keyboard command is required to start.

This applies to stub and physical controller modes. Startup starts no video capture and performs
no camera movements. Camera controls require driver support for IAMCameraControl and manual Zoom.
Optional Pan/Tilt may be unavailable without preventing Zoom initialization.

There are no rescans, retries, reconnections or live camera switching. After an error, Active becomes
false and the worker is cancelled. Check logger.txt and restart the application. Opening menus only
shows the startup device list and current session status; it does not retry discovery or initialization.

Select a camera in Cameras to save Name/DevicePath in
`%LocalAppData%\LogitechCameraBridge\userSettings.json`. After a successful save, the application
automatically restarts and initializes the chosen camera. A failed save does not restart the app.
Use Restart (directly above Exit) to restart manually, including after a device or control error.
Preferences survive builds and remain separate from shared appSettings.json. Unknown JSON settings
are preserved; invalid JSON is reported without overwriting it. Failed saves retain the old choice.

### Status

| Row | Values |
| --- | --- |
| Logitech Controller | Stub, <device name>, Not found, or Error |
| Camera | <camera name>, Not selected, or Error |
| Active | true or false |

Missing devices, no selection, errors and Active: false are red. Status updates from initialization
and observed worker events without menu-triggered rescans. A found camera alone does not establish
readiness. Errors remain until restart. Unsupported optional axes during initialization do not block Zoom.

The tray icon uses a Microsoft Fluent camera glyph inside a status circle:

- Green: the bridge is ready and Active is true.
- Red: a controller/device/initialization/runtime error, or an otherwise inactive completed startup.
- Orange: the controller is found and the only issue is no available saved camera selection.
- Gray: initialization is still in progress; readiness has not been confirmed.

Errors take priority over orange. The tooltip also names the state. The icon has a light background
and transparent outer corners for contrast on light/dark taskbars. The glyph and its MIT license
are included under `src/LogitechCameraBridge.Wpf/Assets`; the license is copied to build/publish output.
Source: [Microsoft Fluent System Icons](https://github.com/microsoft/fluentui-system-icons).

### Camera controls

The Camera controls submenu contains three read-only rows, for example:

```text
Zoom: 100 (min: 100, max: 400)
Pan: 0 (min: -16, max: 16)
Tilt: 0 (min: -16, max: 16)
```

The number is the last value read from the driver; parentheses show min and max labels. Values are read during
normal bridge initialization and after commands, including a read after each successful Set. An
attempt at a limit refreshes the value without another Set. No timer, video capture or additional
movement is used for this display. Changes made by other apps appear after the next bridge command
for that axis; opening the submenu does not poll the camera.

Unavailable axes show Not supported (manual control unavailable or an explicit unsupported response).
Unexpected driver failures/invalid ranges show Error in red; details are logged. Before an axis has
been examined, it shows Not checked. All axes are reported even when required Zoom is unavailable;
Zoom remains required for startup and unavailable optional Pan/Tilt do not block Zoom.
These are driver-reported capabilities, not proof of physical movement. The submenu follows the
startup camera; choosing another camera saves/restarts the application as before.

### Keyboard simulator

With UseControllerStub: true, commands work only after startup reports Active: true:

| Shortcut | Action |
| --- | --- |
| Hold Z, press main +/= key or numpad + | Zoom In |
| Hold Z, press main - key or numpad - | Zoom Out |
| Hold P, press Left/Right | Pan Left/Right |
| Hold P, press Up/Down | Tilt Up/Down |

Press the prefix first. Z/P are Windows virtual keys (same key positions in non-Latin layouts).
Shifted main + is supported. The keyboard hook recognizes global chords only in stub mode and
logs no text. Z/P and ordinary keys pass through; action keys, repeats and releases are consumed.
Ctrl/Alt/Windows modifiers, both prefixes held, or injected input do not generate commands.
One press submits one configured report without auto-repeat. Keys never start/restart/switch the bridge.
Use a preview window to observe actual movement. Do not run console and WPF bridges simultaneously.

Restart and Exit remove the hook, cancel startup/processing, await HID/COM cleanup and pending
settings writes, and flush logs. Restart launches a replacement instance that waits for the old
process to exit before creating its tray icon or initializing devices. Restart works for apphost
and dotnet-hosted launches, without opening a console window. The original dotnet run command may
return to its terminal while the replacement continues running in the tray. Relaunch failures are
logged and the old instance exits; launch the app manually if that happens.
Startup configuration failures leave Restart and Exit available.
About contains a read-only Version row and Check for updates. Version comes from the WPF assembly's
AssemblyVersion and uses exactly three components (major.minor.build). Check for updates currently
does nothing; Velopack integration is planned. Future GitHub Actions release builds can set
AssemblyVersion through MSBuild properties; no release workflow or update client is added yet.

Manual check:

1. Start with a saved available camera; inspect Status without pressing simulator keys.
2. Expect Active: true after successful initialization, or an explicit device/error status.
3. Select a different camera; confirm that the app restarts automatically and uses the saved camera.
4. Start with an unavailable saved camera: expect Not selected and Active: false.
5. Choose Restart; verify that the old instance exits and one replacement tray icon appears.
6. After an error, inspect logs and use Restart. Status/keyboard input must not retry initialization.
7. Exit or restart during startup/processing; confirm clean cleanup and no overlapping device sessions.

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
Driver errors are logged. The console continues processing; the WPF host stops and requires Restart.
Release (0x00), unknown (0xFF), Far End, presets, and unmapped codes do not change the camera.
There is no application-generated button hold/repeat behavior yet.

## Hardware verification

The user confirmed all six commands (Zoom In/Out, Pan Left/Right, Tilt Up/Down) with the keyboard
controller stub and the integrated camera on a Lenovo Legion 7 16IAX10 on 2026-11-19.
The supplied initialization log reports manual control for all three axes:

| Property | Min | Max | Step | Default |
| --- | --- | --- | --- | --- |
| Zoom | 100 | 400 | 10 | 100 |
| Pan | -16 | 16 | 1 | 0 |
| Tilt | -16 | 16 | 1 | 0 |

One Zoom press therefore changes the value by 10 on this camera. Other drivers may report different steps.
This confirms the keyboard-stub-to-real-camera path; it does not establish physical-controller
Pan/Tilt acceptance, range-limit behavior, or compatibility with other cameras.

Earlier user tests confirmed Zoom control during an active Zoom conference and normal
volume/microphone operation with the physical controller. Exact image directions, boundary cases,
and Pan/Tilt behavior during a conference remain separate checks.

1. Start the app with logging enabled and inspect the Pan/Tilt capability output.
2. Increase zoom within the camera's reported range and observe a separately opened camera preview.
3. Press each arrow briefly. Confirm that Left/Right move in the expected horizontal direction
   and Up/Down move in the expected vertical direction. Record the reported values and visible movement.
4. Repeat at minimum zoom to compare behavior. The app does not adjust zoom automatically.
5. Test both limits on each supported axis and verify Zoom still works afterwards.
6. Repeat during a Zoom conference and check volume/microphone operation.
7. Press Ctrl+C. HID reads use a one-second timeout to check cancellation.

Directions use Right/Up as positive initially; actual camera image movement must be verified,
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
