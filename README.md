# VoiceDictationCodex

VoiceDictationCodex is a Windows-first, offline voice dictation studio that wraps local Whisper models in a modern desktop experience inspired by Wispr Flow and typeless. The application is designed for creators, developers, and professionals who need accurate speech-to-text without ever sending data to the cloud.

## Highlights

- **Fully offline pipeline** – select, download, and run Whisper GGML models locally.
- **Session-focused workflow** – record live audio, import files, revisit transcripts, and manage formatting without leaving the dashboard.
- **Automatic session vault** – each capture lands in a timestamped folder with the original audio, raw output, formatted transcript, and metadata for easy archival.
- **Elegant UI** – WPF-based layout with dark, glassy accents and chip indicators that echo leading productivity apps.
- **Extensible services** – modular services for model catalog, downloads, audio capture, and Whisper runtime orchestration.
- **Formatting controls** – toggle auto punctuation, smart spacing, and capitalisation to match your publishing style.

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
     - Alternatively set the `VOICEDICTATION_WHISPER_PATH` environment variable to the executable location
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

5. **Audio capture & transcription**
   - The app uses [`NAudio`](https://github.com/naudio/NAudio) for microphone input.
   - Press **Start Recording** to capture audio into a 16 kHz mono WAV file, saved in `%USERPROFILE%\Documents\VoiceDictationCodex\Sessions`.
   - Press **Stop Recording** to automatically launch the configured Whisper runtime and stream JSON output back into the UI.
   - Use **Import Audio** to copy an existing file into the active session folder and run it through the same offline pipeline.
   - Rename or delete sessions from the history sidebar and export the transcript as TXT or Markdown at any point.
   - Ensure end users have a working microphone and grant capture permissions.

## Session vault

Each session gets its own folder under `%USERPROFILE%\Documents\VoiceDictationCodex\Sessions`. The vault stores:

- `session.json` – metadata about the session, current status, model, and statistics.
- `raw-transcript.txt` – unformatted Whisper output to keep a lossless copy for future processing.
- `transcript.txt` – formatted transcript that respects the in-app formatting toggles.
- Imported or recorded audio, preserved alongside exports.

The main dashboard surfaces the entire vault so you can jump back into any session, continue dictating, or export again without hunting through the file system.

## Next steps

- Integrate `whisper.cpp.exe` or another local inference binary and stream audio chunks from `AudioCaptureService` directly.
- Implement waveform visualization and editing tools for imported files.
- Add settings view for hotkeys, output formatting, and auto punctuation.
- Package with MSIX/WinGet installer including optional pre-downloaded models.
