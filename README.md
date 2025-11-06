# VoiceDictationCodex

VoiceDictationCodex is a Windows-first, offline voice dictation studio that wraps local Whisper models in a modern desktop experience inspired by Wispr Flow and typeless. The application is designed for creators, developers, and professionals who need accurate speech-to-text without ever sending data to the cloud.

## Highlights

- **Fully offline pipeline** – select, download, and run Whisper GGML models locally.
- **Session-focused workflow** – record live audio, import files, and manage transcripts from a unified dashboard.
- **Elegant UI** – WPF-based layout with dark, glassy accents and chip indicators that echo leading productivity apps.
- **Extensible services** – modular services for model catalog, downloads, audio capture, and Whisper runtime orchestration.

## Project structure

```
src/
  VoiceDictationCodex.App/
    Models/              # View-state and metadata records
    Services/            # Model management, audio capture, Whisper runtime
    ViewModels/          # MVVM layer powering the UI
    Views/               # WPF XAML views
    Resources/           # Shared theme resources
```

## Getting started

1. **Prerequisites**
   - Windows 10/11
   - .NET 8 SDK with desktop workload installed
   - `whisper.cpp.exe` packaged alongside the app (see `WhisperRuntime` service)
   - Optional: Visual Studio 2022 or Visual Studio Code with C# extension

2. **Restore and build**
   ```bash
   dotnet restore src/VoiceDictationCodex.App/VoiceDictationCodex.App.csproj
   dotnet build src/VoiceDictationCodex.App/VoiceDictationCodex.App.csproj
   ```

3. **Run**
   ```bash
   dotnet run --project src/VoiceDictationCodex.App/VoiceDictationCodex.App.csproj
   ```

4. **Model hosting**
   - The default download endpoint targets the public `whisper.cpp` GGML files on Hugging Face.
   - Replace the `ModelDownloadService.GetDownloadUrl` implementation if you maintain your own mirror or ship models pre-bundled.

5. **Audio capture**
   - The app uses [`NAudio`](https://github.com/naudio/NAudio) for microphone input.
   - Ensure end users have a working microphone and grant capture permissions.

## Next steps

- Integrate `whisper.cpp.exe` or another local inference binary and stream audio chunks from `AudioCaptureService` directly.
- Implement waveform visualization and editing tools for imported files.
- Add settings view for hotkeys, output formatting, and auto punctuation.
- Package with MSIX/WinGet installer including optional pre-downloaded models.
