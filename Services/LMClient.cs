using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ModelHub.Models;
using System.Diagnostics;
using System.Linq;

namespace ModelHub.Services;

public class LMClient : LMInterface
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10000)
    };

    public async Task<IReadOnlyList<MLModel>> GetModelsAsync(string baseUrl)
    {
        var url = ToNativeApiBaseUrl(baseUrl) + "/models";

        var response = await _httpClient.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"LM Studio returned {(int)response.StatusCode}: {json}");
        }

        var models = new List<MLModel>();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        JsonElement modelsArray;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("models", out var modelsElement) &&
            modelsElement.ValueKind == JsonValueKind.Array)
        {
            modelsArray = modelsElement;
        }
        else if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("data", out var dataElement) &&
                dataElement.ValueKind == JsonValueKind.Array)
        {
            modelsArray = dataElement;
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            modelsArray = root;
        }
        else
        {
            throw new Exception($"Could not find model list in LM Studio response: {json}");
        }

        foreach (var item in modelsArray.EnumerateArray())
        {
            var id = "";

            if (item.TryGetProperty("id", out var idElement))
            {
                id = idElement.GetString() ?? "";
            }
            else if (item.TryGetProperty("key", out var keyElement))
            {
                id = keyElement.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var displayName = item.TryGetProperty("display_name", out var displayNameElement)
                ? displayNameElement.GetString() ?? id
                : id;

            var isLoaded = false;
            var instanceId = "";

            if (item.TryGetProperty("loaded_instances", out var loadedInstancesElement) &&
                loadedInstancesElement.ValueKind == JsonValueKind.Array &&
                loadedInstancesElement.GetArrayLength() > 0)
            {
                isLoaded = true;

                var firstInstance = loadedInstancesElement[0];

                if (firstInstance.TryGetProperty("id", out var instanceIdElement))
                {
                    instanceId = instanceIdElement.GetString() ?? "";
                }
            }

            models.Add(new MLModel
            {
                Id = id,
                InstanceId = instanceId,
                OwnedBy = item.TryGetProperty("publisher", out var publisherElement)
                    ? publisherElement.GetString() ?? ""
                    : "",

                IsLoaded = isLoaded,
                LastStatus = isLoaded ? "Loaded" : "Ejected",
                LastElapsedMilliseconds = 0
            });
        }

        return models;
    }

    public async Task<bool> LoadModelAsync(string baseUrl, string modelId)
    {
        var url = ToNativeApiBaseUrl(baseUrl) + "/models/load";

        var body = new
        {
            model = modelId
        };

        var response = await _httpClient.PostAsJsonAsync(url, body);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UnloadModelAsync(string baseUrl, string instanceId)
    {
        var url = ToNativeApiBaseUrl(baseUrl) + "/models/unload";

        var body = new
        {
            instance_id = instanceId
        };

        var response = await _httpClient.PostAsJsonAsync(url, body);

        return response.IsSuccessStatusCode;
    }

    public async Task<PromptResult> SendPromptAsync(string baseUrl, PromptRequest request)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var url = NormalizeBaseUrl(baseUrl) + "/chat/completions";

            var body = new
            {
                model = request.ModelId,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "You are a helpful assistant."
                    },
                    new
                    {
                        role = "user",
                        content = request.Prompt
                    }
                },
                temperature = request.Temperature,
                max_tokens = request.MaxTokens,
                stream = false
            };

            var response = await _httpClient.PostAsJsonAsync(url, body);
            var json = await response.Content.ReadAsStringAsync();

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return new PromptResult
                {
                    ModelId = request.ModelId,
                    Success = false,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                    ErrorMessage = json
                };
            }

            var chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            var text = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "";

            return new PromptResult
            {
                ModelId = request.ModelId,
                ResponseText = text,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                Success = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new PromptResult
            {
                ModelId = request.ModelId,
                Success = false,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<PromptResult> SendVisionPromptAsync(
        string baseUrl,
        string modelId,
        string systemPrompt,
        string userText,
        IReadOnlyList<string> imageDataUrls)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var url = NormalizeBaseUrl(baseUrl) + "/chat/completions";

            var userContent = new List<object>
            {
                new
                {
                    type = "text",
                    text = userText
                }
            };

            foreach (var imageDataUrl in imageDataUrls)
            {
                userContent.Add(new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = imageDataUrl,
                        detail = "low"
                    }
                });
            }

            var body = new
            {
                model = modelId,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = systemPrompt
                    },
                    new
                    {
                        role = "user",
                        content = userContent
                    }
                },
                temperature = 0.2,
                max_tokens = 250,
                stream = false
            };

            var response = await _httpClient.PostAsJsonAsync(url, body);
            var json = await response.Content.ReadAsStringAsync();

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return new PromptResult
                {
                    ModelId = modelId,
                    Success = false,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                    ErrorMessage = json
                };
            }

            var chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            var text = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "";

            return new PromptResult
            {
                ModelId = modelId,
                ResponseText = text.Trim(),
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                Success = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new PromptResult
            {
                ModelId = modelId,
                Success = false,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return baseUrl.Trim().TrimEnd('/');
    }

    private class ModelsResponse
    {
        [JsonPropertyName("data")]
        public List<ModelItem> Data { get; set; } = new();
    }

    private class ModelItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("owned_by")]
        public string? OwnedBy { get; set; }
    }

    private class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice> Choices { get; set; } = new();
    }

    private class Choice
    {
        [JsonPropertyName("message")]
        public ChatMessage Message { get; set; } = new();
    }

    private class ChatMessage
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private static string ToNativeApiBaseUrl(string baseUrl)
    {
        var clean = baseUrl.Trim().TrimEnd('/');

        if (clean.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[..^3];
        }

        return clean + "/api/v1";
    }
}