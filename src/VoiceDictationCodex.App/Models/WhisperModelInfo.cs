namespace VoiceDictationCodex.Models;

public record WhisperModelInfo(
    string Id,
    string Name,
    string Description,
    double DownloadSize,
    string LocalPath,
    string LanguageSupport,
    bool IsInstalled);
