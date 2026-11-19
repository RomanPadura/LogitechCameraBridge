# Camera icon

FluentCamera.svg is Microsoft's Fluent System Icons `ic_fluent_camera_24_filled.svg`:
https://github.com/microsoft/fluentui-system-icons/blob/main/assets/Camera/SVG/ic_fluent_camera_24_filled.svg

Copyright (c) 2020 Microsoft Corporation. Distributed under the MIT License in
FluentCamera.LICENSE.txt. The application adds its own background and status ring at runtime.

`LogitechCameraBridge.ico` reuses the green (Healthy) tray rendering for the executable,
installer and Windows Installed Apps entry. It contains 16, 20, 24, 32, 48, 64, 128
and 256 pixel images. The Fluent icon attribution/license above also applies to this derivative.

Regenerate it from the repository root after changing the tray artwork:

```powershell
dotnet run --project tools/GenerateAppIcon/GenerateAppIcon.csproj -c Release -- src/LogitechCameraBridge.Wpf/Assets/LogitechCameraBridge.ico
```
