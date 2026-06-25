using System.Collections.Generic;
using System.Threading.Tasks;
using ModelHub.Models;

namespace ModelHub.Services;

public interface LMInterface
{
    Task<IReadOnlyList<MLModel>> GetModelsAsync(string baseUrl);
    Task<PromptResult> SendPromptAsync(string baseUrl, PromptRequest request);

    Task<bool> LoadModelAsync(string baseUrl, string modelId);
    Task<bool> UnloadModelAsync(string baseUrl, string modelId);

    Task<PromptResult> SendVisionPromptAsync(
    string baseUrl,
    string modelId,
    string systemPrompt,
    string userText,
    IReadOnlyList<string> imageDataUrls);
}