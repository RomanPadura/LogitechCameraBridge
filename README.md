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

## WPF foundation (in progress)

`src/LogitechCameraBridge.Wpf` targets `net10.0-windows` and uses CommunityToolkit.Mvvm,
DI and options from the same linked `LogitechCameraBridgeConsole/appSettings.json`.
The project currently builds as a library while the window design is being specified;
there is no executable WPF window or WPF launch command yet.

The foundation includes a dedicated MTA bridge worker, cancellation, overlapping-run protection,
and a ViewModel with Start/Stop commands, readiness status and error reporting.
Running status requires camera initialization and an open HID stream; it is not inferred from
the requested start operation. It is a startup readiness signal, not continuous connection monitoring.
Stop and `ShutdownAsync` wait for the worker to release device resources. Window-close integration
and application startup/shutdown (including logger flush) remain to be connected with the window.
Do not run the console host and the future WPF host simultaneously during hardware tests.

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
