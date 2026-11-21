# LogitechCameraBridge

Windows tray and console bridge from Logitech GROUP speakerphone HID buttons to cameras
with driver-exposed Zoom, Pan or Tilt controls through IAMCameraControl.
Targets .NET 10. No video capture is started by the bridge.

## Supported controller models

| Controller | Model number | USB VID / PID | Verification |
| --- | --- | --- | --- |
| Logitech GROUP speakerphone | V-U0036 (observed P/N 886-000056) | `046D:0857` | Physical device discovery and Zoom In/Out button presses confirmed. Physical Pan/Tilt and hold-to-repeat on the current build still need verification. |

The GROUP speakerphone is currently the only verified physical controller model.
The bridge reads its camera-control HID collection: interface `MI_03`, collection
`COL01`, Usage Page `0xFF00`, Usage `0x0001`, Report ID `0x0A`.
Timed hardware traces show a direction report on press followed by `0x00` about
15 ms later, even while held. `0x00` is neutral and does not stop movement.
Physical release sends `0xFF`, followed by `0x00`; the app stops on `0xFF`.
The app generates movement repeats itself during USB silence.

Logitech identifies V-U0036 as the GROUP speakerphone in its
[official GROUP specifications](https://hub.sync.logitech.com/group/post/group-specifications-VzQTp3uaMw10y6I).
Other controller models and the separate GROUP infrared remote have not been
verified with this bridge. Changing VID/PID alone does not establish compatibility;
the HID collection and report format must also match.

The controlled camera is a separate device: Logitech C930e is a tested camera,
not an additional controller model. See [Hardware verification](#hardware-verification)
for camera test results.

## Run

```powershell
dotnet build LogitechCameraBridgeSln.sln
dotnet run --project src/LogitechCameraBridgeConsole
```

Edit `src/LogitechCameraBridgeConsole/appSettings.json` before starting the app.
All button codes are hexadecimal strings in `BridgeControllerDevice`.
Set `Logger.LogToConsole` to `true` to see discovery, ranges, reports, and errors.
Restart the app after configuration changes.

Camera USB selection is defined only in the console's `Program.cs` (Logitech C930e:
VID `0x046D`, PID `0x0843`, interface `MI_00`). Change those console defaults there
when using another USB camera. The shared JSON has no `CameraToControl` section;
the tray app uses the camera selected through its menu.

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

The notification-area icon uses the Windows Shell API with a hidden message window;
all menus use WPF controls. There is no WinForms dependency. The icon is restored
automatically after Windows Explorer restarts, and removed when the app exits.
Both left-click and right-click on the tray icon open the same menu; keyboard activation also works.

Only one bridge instance can run per Windows user/session, shared by the tray and
console apps and across install/build folders. A second tray launch exits quietly
before creating a tray icon or opening devices; a second console launch prints an
already-running message. Exit the running app before switching hosts.
Restart and updates can start a replacement after the previous process exits.
Windows releases ownership if the process crashes, so there is no stale lock file to remove.

**Starts with OS: true/false** is a submenu with **Activate** and **Deactivate**
actions (off by default). The action matching the current state is disabled. It registers the
app for the current Windows user at sign-in, without administrator rights, using
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\LogitechCameraBridge`.
The command points to the package's stable root `Logitech Camera Bridge.exe` launcher so it survives
app updates. Deactivate removes the entry; uninstall also attempts to remove it.
The preference is read from the registry and does not change `userSettings.json`.
Existing `Update.exe start` registrations for this installation migrate to the named
launcher when the updated app starts. The registration's value name is retained, so
Windows' separate disabled override is preserved. Startup remains off if it was not registered.

The submenu is visible only in an installed release. It is hidden in development
builds and portable packages, even if an older startup entry exists.
If you disable the app in Windows Startup Apps/Task Manager, re-enable it there;
the tray status reflects the app's Run registration, not Windows' separate override.

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
`%LocalAppData%\LogitechCameraBridgeData\userSettings.json`. After a successful save, the application
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
One press submits a configured press report followed by release, without keyboard auto-repeat.
Keys never start/restart/switch the bridge.
Use a preview window to observe actual movement. Exit the WPF bridge before starting
the console (or vice versa); the single-instance guard prevents running both simultaneously.

Restart and Exit remove the hook, cancel startup/processing, await HID/COM cleanup and pending
settings writes, and flush logs. Restart launches a replacement instance that waits for the old
process to exit before creating its tray icon or initializing devices. Restart works for apphost
and dotnet-hosted launches, without opening a console window. The original dotnet run command may
return to its terminal while the replacement continues running in the tray. Relaunch failures are
logged and the old instance exits; launch the app manually if that happens.
Startup configuration failures leave Restart and Exit available.
About contains a read-only Version row and Check for updates. Version comes from the WPF assembly's
AssemblyVersion and uses exactly three components (major.minor.build). Check for updates checks
GitHub Releases and silently installs a newer stable Velopack package, then restarts the app.
The tag-based release workflow injects the package version into the application at build time.

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
Hold a physical Zoom/Pan/Tilt button to keep moving: one immediate driver step, then
repetition using the JSON timings below (defaults: 400 ms initial delay, 100 ms interval).
Release (`0xFF`) stops the movement. Duplicate
press reports do not accelerate it. Movement also stops at a limit or on a driver failure;
press again after release to try again. Unmapped codes cancel the current hold; `0x00` is ignored.
Disconnecting the controller or shutting down the bridge stops the worker and its repeats.

Far End (`0x40`) and presets do not move the camera. Far End reset is deferred.

To verify on the physical controller: hold Zoom In for one second, release and confirm it
stops; repeat for Zoom Out and the directional buttons.
Keyboard simulation remains one step per chord; it does not emulate physical holding.

Configure both timings in the root-level `ButtonRepeat` section of `appSettings.json`
(shared by the Console and WPF hosts):

```json
"ButtonRepeat": {
  "RepeatIntervalMs": 100,
  "InitialDelayMs": 400
}
```

`RepeatIntervalMs` is the interval between repeated movements; `InitialDelayMs` is the
delay from the first press to the first repeat. Each accepts whole milliseconds from
1 to 60,000. A missing, null, nonnumeric, fractional, zero, negative or out-of-range
value independently falls back to 100 ms for the interval or 400 ms for the delay.
The JSON document itself must still have valid syntax. Restart the app after editing.
These timings apply to all three axes and do not change the driver step size.
Camera-call duration and HID polling can make the actual repeat rate slower.

For hold diagnostics, rebuild and launch the console from this checkout. With default timings it prints
`Hold-to-repeat enabled: initial delay=400ms, interval=100ms, HID read timeout=50ms.`
The corrected build also prints `GROUP release mapping: 0x00=neutral, 0xFF=release.`
Logs include monotonic HID receive times, repeat attempts and the elapsed time from press
to release. Silence and unrelated HID report IDs both allow repeat ticks to run.
Older console builds listing only `Zoom In` / `Zoom Out` without Pan/Tilt initialization
do not test the current hold implementation.

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
## Application updates

Release packages require **.NET 10 Desktop Runtime for Windows x64**. Setup.exe
can download/install it when missing. For portable ZIP use, install it first from
[Microsoft's .NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

The tray app supports Velopack updates from GitHub Releases through **About > Check
for updates...**. See [the step-by-step release and update guide](docs/UPDATES.md)
for repository visibility, first installation, version tags and silent updates.
