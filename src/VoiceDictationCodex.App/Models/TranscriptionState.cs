using System;

namespace VoiceDictationCodex.Models;

public class TranscriptionState
{
    public string Title { get; set; } = "Untitled Session";
    public string Text { get; set; } = string.Empty;
    public string RawText { get; set; } = string.Empty;
    public string Duration { get; set; } = "00:00";
    public int WordCount { get; set; }
    public string Language { get; set; } = "Auto";
    public string ModelName { get; set; } = "";
    public string SessionFolder { get; set; } = string.Empty;
    public string? SourceAudioPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastUpdated { get; set; }
    public bool IsCompleted { get; set; }
}
