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

Run from the VS Code PowerShell terminal:

```powershell
dotnet run --project src/LogitechCameraBridge.Wpf
```

The app uses .NET 10 WPF, MVVM, DI and the shared console appSettings.json. It has no startup
window or taskbar button. Its tray menu contains Status, Cameras and Exit.

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
`%LocalAppData%\LogitechCameraBridge\userSettings.json`, then restart to use it. The running camera
remains the startup choice. The menu confirms that the new choice is saved for the next startup.
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

Exit removes the hook, cancels startup/processing, awaits HID/COM cleanup and pending settings writes,
flushes logs and disposes the icon/services. Startup configuration failures leave Exit available.
About and Update remain future steps.

Manual check:

1. Start with a saved available camera; inspect Status without pressing simulator keys.
2. Expect Active: true after successful initialization, or an explicit device/error status.
3. Save a different camera, exit and restart; confirm the new startup camera is used.
4. Start with an unavailable saved camera: expect Not selected and Active: false.
5. After a startup/runtime error, check logs and restart. Menus/keys must not cause a retry.
6. Exit during startup/processing; confirm the process and icon disappear cleanly.

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
