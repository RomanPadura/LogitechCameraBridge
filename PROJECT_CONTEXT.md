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
No reset, hold/repeat, automatic zoom adjustment, reconnection, or WPF UI is implemented yet.

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
