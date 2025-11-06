using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VoiceDictationCodex.Models;

namespace VoiceDictationCodex.Services;

public class SessionVaultService
{
    private const string MetadataFileName = "session.json";
    private const string TranscriptFileName = "transcript.txt";
    private const string RawTranscriptFileName = "raw-transcript.txt";

    private readonly string _rootPath;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public SessionVaultService(string? rootPath = null)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _rootPath = rootPath ?? Path.Combine(documents, "VoiceDictationCodex", "Sessions");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<SessionEntry> CreateSessionAsync(string? displayName = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var folderName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{id[..6]}";
        var sessionFolder = Path.Combine(_rootPath, folderName);
        Directory.CreateDirectory(sessionFolder);

        var entry = new SessionEntry
        {
            Id = id,
            DisplayName = displayName ?? $"Session {DateTime.Now:MMM d, HH:mm}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SessionFolder = sessionFolder,
            TranscriptPath = Path.Combine(sessionFolder, TranscriptFileName),
            RawTranscriptPath = Path.Combine(sessionFolder, RawTranscriptFileName),
            Status = SessionEntryStatus.Empty
        };

        await SaveMetadataAsync(entry);
        return entry;
    }

    public async Task<IReadOnlyList<SessionEntry>> GetSessionsAsync()
    {
        var sessions = new List<SessionEntry>();
        if (!Directory.Exists(_rootPath))
        {
            return sessions;
        }

        foreach (var directory in Directory.EnumerateDirectories(_rootPath))
        {
            var metadataPath = Path.Combine(directory, MetadataFileName);
            SessionEntry? entry = null;
            if (File.Exists(metadataPath))
            {
                await using var stream = File.OpenRead(metadataPath);
                var metadata = await JsonSerializer.DeserializeAsync<SessionMetadata>(stream, _serializerOptions);
                if (metadata is not null)
                {
                    entry = MapToEntry(metadata, directory);
                }
            }

            if (entry is null)
            {
                entry = new SessionEntry
                {
                    SessionFolder = directory,
                    DisplayName = Path.GetFileName(directory) ?? "Session",
                    CreatedAt = Directory.GetCreationTimeUtc(directory),
                    TranscriptPath = Path.Combine(directory, TranscriptFileName),
                    RawTranscriptPath = Path.Combine(directory, RawTranscriptFileName),
                    Status = SessionEntryStatus.Empty
                };
            }

            sessions.Add(entry);
        }

        return sessions
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .ToList();
    }

    public async Task<SessionEntry> SaveSnapshotAsync(SessionEntry session, string rawTranscript, string formattedTranscript, TimeSpan duration, int wordCount, string? model, string? language, string? audioPath, bool markCompleted)
    {
        Directory.CreateDirectory(session.SessionFolder);

        var transcriptPath = session.TranscriptPath ?? Path.Combine(session.SessionFolder, TranscriptFileName);
        var rawTranscriptPath = session.RawTranscriptPath ?? Path.Combine(session.SessionFolder, RawTranscriptFileName);

        await File.WriteAllTextAsync(transcriptPath, formattedTranscript ?? string.Empty);
        await File.WriteAllTextAsync(rawTranscriptPath, rawTranscript ?? formattedTranscript ?? string.Empty);

        session.TranscriptPath = transcriptPath;
        session.RawTranscriptPath = rawTranscriptPath;
        session.Duration = duration;
        session.WordCount = wordCount;
        session.Model = model;
        session.Language = language;
        session.SourceAudioPath = audioPath;
        session.HasTranscript = !string.IsNullOrWhiteSpace(formattedTranscript);
        session.Status = markCompleted
            ? SessionEntryStatus.Completed
            : session.HasTranscript
                ? SessionEntryStatus.InProgress
                : !string.IsNullOrWhiteSpace(audioPath)
                    ? SessionEntryStatus.InProgress
                    : session.Status;
        session.UpdatedAt = DateTime.UtcNow;

        await SaveMetadataAsync(session);
        return session;
    }

    public async Task<SessionContent> ReadContentAsync(SessionEntry session)
    {
        var transcriptPath = session.TranscriptPath ?? Path.Combine(session.SessionFolder, TranscriptFileName);
        var rawTranscriptPath = session.RawTranscriptPath ?? Path.Combine(session.SessionFolder, RawTranscriptFileName);

        string? raw = null;
        string? formatted = null;

        if (File.Exists(rawTranscriptPath))
        {
            raw = await File.ReadAllTextAsync(rawTranscriptPath);
        }

        if (File.Exists(transcriptPath))
        {
            formatted = await File.ReadAllTextAsync(transcriptPath);
        }

        return new SessionContent(raw, formatted);
    }

    public async Task RenameSessionAsync(SessionEntry session, string newName)
    {
        session.DisplayName = newName;
        session.UpdatedAt = DateTime.UtcNow;
        await SaveMetadataAsync(session);
    }

    public Task DeleteSessionAsync(SessionEntry session)
    {
        if (Directory.Exists(session.SessionFolder))
        {
            Directory.Delete(session.SessionFolder, recursive: true);
        }

        return Task.CompletedTask;
    }

    public async Task<SessionEntry> UpdateStatusAsync(SessionEntry session, SessionEntryStatus status)
    {
        session.Status = status;
        session.UpdatedAt = DateTime.UtcNow;
        await SaveMetadataAsync(session);
        return session;
    }

    private async Task SaveMetadataAsync(SessionEntry session)
    {
        Directory.CreateDirectory(session.SessionFolder);
        var metadataPath = Path.Combine(session.SessionFolder, MetadataFileName);
        var metadata = new SessionMetadata
        {
            Id = session.Id,
            DisplayName = session.DisplayName,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            TranscriptPath = session.TranscriptPath,
            RawTranscriptPath = session.RawTranscriptPath,
            SourceAudioPath = session.SourceAudioPath,
            Duration = session.Duration,
            WordCount = session.WordCount,
            Model = session.Model,
            Language = session.Language,
            Status = session.Status,
            HasTranscript = session.HasTranscript
        };

        await using var stream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(stream, metadata, _serializerOptions);
    }

    private static SessionEntry MapToEntry(SessionMetadata metadata, string sessionFolder)
        => new()
        {
            Id = metadata.Id ?? Guid.NewGuid().ToString("N"),
            DisplayName = metadata.DisplayName ?? Path.GetFileName(sessionFolder) ?? "Session",
            CreatedAt = metadata.CreatedAt == default ? Directory.GetCreationTimeUtc(sessionFolder) : metadata.CreatedAt,
            UpdatedAt = metadata.UpdatedAt,
            SessionFolder = sessionFolder,
            TranscriptPath = metadata.TranscriptPath ?? Path.Combine(sessionFolder, TranscriptFileName),
            RawTranscriptPath = metadata.RawTranscriptPath ?? Path.Combine(sessionFolder, RawTranscriptFileName),
            SourceAudioPath = metadata.SourceAudioPath,
            Duration = metadata.Duration,
            WordCount = metadata.WordCount,
            Model = metadata.Model,
            Language = metadata.Language,
            Status = metadata.Status,
            HasTranscript = metadata.HasTranscript
        };

    private class SessionMetadata
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? TranscriptPath { get; set; }
        public string? RawTranscriptPath { get; set; }
        public string? SourceAudioPath { get; set; }
        public TimeSpan Duration { get; set; }
        public int WordCount { get; set; }
        public string? Model { get; set; }
        public string? Language { get; set; }
        public SessionEntryStatus Status { get; set; }
        public bool HasTranscript { get; set; }
    }
}

public readonly record struct SessionContent(string? Raw, string? Formatted);
