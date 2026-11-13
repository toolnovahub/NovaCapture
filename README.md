# NovaCapture v0.1

Windows 10/11 screenshot and screen recording utility. C# / .NET 8 / WPF.

## Features
- PNG screenshot of the entire virtual desktop (all connected monitors).
- MP4 video recording of the primary monitor, 30 FPS, no audio.
- Custom output folder and open-folder button.
- Dark UI, visible recording status, Start/Stop controls.
- No network upload, telemetry, or hidden recording.

## Build on Windows
Install .NET 8 SDK, then from this folder run:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Or upload project to a **new** GitHub repository and use Actions > Build NovaCapture Windows > Run workflow. Download the `NovaCapture-Windows-x64` artifact. This project is not connected to ToolNovaHub.

## FFmpeg required for MP4 recording
Install an FFmpeg Windows build containing `gdigrab` and `libx264`; place `ffmpeg.exe` beside `NovaCapture.exe`, or put its folder in PATH. FFmpeg is not bundled. Consult the license of your FFmpeg distribution before redistributing it.

## Known limitations
- No audio in v0.1.
- Video captures the primary monitor and includes the app window if visible.
- Screenshot captures all monitors, and the app briefly hides itself.
- Windows may require screen-capture permissions; some protected content cannot be captured.
- No region selection, hotkeys, tray mode, pause/resume, or recording timer yet.
- The project has not been compiled or runtime-tested in this environment; test on Windows before distribution.