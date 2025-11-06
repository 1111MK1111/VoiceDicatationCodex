using VoiceDictationCodex.Models;

namespace VoiceDictationCodex.Services;

public interface IModelRepository
{
    Task<IReadOnlyList<WhisperModelInfo>> LoadAvailableModelsAsync(CancellationToken cancellationToken = default);
    Task<WhisperModelInfo?> GetInstalledModelAsync(string id, CancellationToken cancellationToken = default);
    Task SaveModelMetadataAsync(WhisperModelInfo model, CancellationToken cancellationToken = default);
}
