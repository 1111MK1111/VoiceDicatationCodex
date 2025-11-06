# VoiceDictationCodex architecture

VoiceDictationCodex follows a lightweight MVVM architecture designed for Windows Presentation Foundation (WPF) applications. The goal is to keep UI, state, and domain concerns separated while making it easy to swap out infrastructure components such as the Whisper runtime or model catalog source.

## Layers

| Layer        | Description |
|--------------|-------------|
| Views        | XAML compositions that define the modern interface (see `Views/MainWindow.xaml`). |
| ViewModels   | Glue between UI and services. Exposes commands and observable state. |
| Models       | Immutable records or change-tracked POCOs (`SessionEntry`, `TranscriptionState`) that describe data flowing through the app. |
| Services     | Long-running or IO-heavy operations such as downloading models, persisting the session vault, recording audio, and orchestrating Whisper. |

## Key flows

### 1. Model management
1. `MainViewModel` loads the static catalog from `ModelCatalogService`.
2. Installed metadata is merged via `IModelRepository` (`JsonModelRepository` implementation).
3. When the user selects **Download Selected**, `ModelDownloadService` streams the GGML file, reports progress, and persists metadata.

### 2. Live transcription & imports
1. `MainViewModel` requests a new session from `SessionVaultService`, which provisions the folder hierarchy and metadata record.
2. `AudioCaptureService` writes microphone input to a 16 kHz mono WAV file within that session folder (or copies imports into place).
3. When recording stops—or an external file is imported—`MainViewModel` invokes `TranscriptionService.TranscribeAsync` with the chosen model and audio path.
4. `WhisperRuntime` locates the `whisper.cpp.exe` binary (or the path specified via `VOICEDICTATION_WHISPER_PATH`), launches it with JSON output enabled, and streams responses.
5. `TranscriptionService` aggregates the incremental text, raises UI updates, and returns the final transcript so the session stats stay in sync while the vault periodically autosaves drafts.

### 3. Session vault & exports
1. `SessionVaultService` persists `session.json`, the raw Whisper output, the formatted transcript, and any imported audio in the session directory.
2. Autosave runs after incremental transcription updates so users can recover mid-session drafts.
3. `MainViewModel` exposes commands for TXT/Markdown export, clipboard copy, and session rename/delete actions surfaced in the sidebar.

## Extending the app

- **Alternate runtimes:** swap `WhisperRuntime` with a managed Whisper.NET or ONNX runtime that runs directly in-process.
- **Additional UI:** add tabbed navigation for a library of historical sessions; the `MainViewModel` can be split into child view models.
- **Telemetry-free analytics:** implement local-only usage metrics by persisting JSON to the app data directory.

## Offline-first considerations

- All downloads default to public mirrors, but you can ship pre-downloaded GGML files and mark them installed by adjusting `JsonModelRepository`.
- To ensure privacy, never send captured audio or transcripts off-device; the provided services keep everything local.
- Consider enabling optional GPU acceleration via DirectML or Vulkan when pairing with GPU-enabled Whisper builds.
