using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelHub.Models;
using ModelHub.Services;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace ModelHub.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly LMInterface _client = new LMClient();

    [ObservableProperty]
    private string baseUrl = "http://localhost:1234/v1";

    [ObservableProperty]
    private string prompt = "";

    [ObservableProperty]
    private string selectedModelId = "";

    [ObservableProperty]
    private MLModel? selectedModel;

    [ObservableProperty]
    private string responseText = "";

    [ObservableProperty]
    private string promptResponseTitle = "Prompt Response";

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string datasetRoot = "";

    partial void OnDatasetRootChanged(string value)
    {
        OnPropertyChanged(nameof(DatasetDisplayText));
    }

    public string DatasetDisplayText =>
        string.IsNullOrWhiteSpace(DatasetRoot)
            ? "No dataset selected"
            : DatasetRoot;

    [ObservableProperty]
    private int maxImagesPerHouse = 10;

    [ObservableProperty]
    private bool isCaptioning;

    [ObservableProperty]
    private string captionProgressText = "No dataset selected.";

    public ObservableCollection<MLModel> Models { get; } = new();

    public ObservableCollection<AppLogEntry> Logs { get; } = new();

    public ObservableCollection<MultiModelResult> ComparisonResults { get; } = new();

    public ObservableCollection<CaptionResult> CaptionResults { get; } = new();

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        StatusMessage = "Testing connection...";
        AddLog("Info", $"Connecting to {BaseUrl}");

        try
        {
            Models.Clear();

            var models = await _client.GetModelsAsync(BaseUrl);

            foreach (var model in models)
            {
                Models.Add(model);
            }

            if (Models.Count > 0)
            {
                SelectedModel = Models[0];

                StatusMessage = $"Connected. Found {Models.Count} model(s).";
                AddLog("Info", $"Found {Models.Count} model(s).");
            }
            else
            {
                StatusMessage = "Connected, but no models were returned.";
                AddLog("Warning", "No models returned from server.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Connection failed.";
            AddLog("Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadModelAsync(MLModel model)
    {
        if (model is null)
        {
            return;
        }

        model.IsRunning = true;
        model.LastStatus = "Loading...";
        StatusMessage = $"Loading model: {model.Id}";
        AddLog("Info", $"Loading model: {model.Id}");

        try
        {
            var success = await _client.LoadModelAsync(BaseUrl, model.Id);

            model.IsLoaded = success;
            model.LastStatus = success ? "Loaded" : "Load failed";

            StatusMessage = success
                ? $"Loaded model: {model.Id}"
                : $"Failed to load model: {model.Id}";

            AddLog(success ? "Info" : "Error", StatusMessage);
        }
        catch (Exception ex)
        {
            model.IsLoaded = false;
            model.LastStatus = "Load failed";
            StatusMessage = $"Failed to load model: {model.Id}";
            AddLog("Error", ex.Message);
        }
        finally
        {
            model.IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task EjectModelAsync(MLModel model)
    {
        if (model is null)
        {
            return;
        }

        model.IsRunning = true;
        model.LastStatus = "Ejecting...";
        StatusMessage = $"Ejecting model: {model.Id}";
        AddLog("Info", $"Ejecting model: {model.Id}");

        try
        {
            if (string.IsNullOrWhiteSpace(model.InstanceId))
            {
                model.IsLoaded = false;
                model.LastStatus = "Ejected";
                StatusMessage = $"Model is already ejected: {model.Id}";
                AddLog("Warning", $"No loaded instance id found for {model.Id}.");
                return;
            }

            var success = await _client.UnloadModelAsync(BaseUrl, model.InstanceId);

            model.IsLoaded = !success;
            model.LastStatus = success ? "Ejected" : "Eject failed";

            if (success)
            {
                model.InstanceId = "";
                await TestConnectionAsync();
            }

            StatusMessage = success
                ? $"Ejected model: {model.Id}"
                : $"Failed to eject model: {model.Id}";

            AddLog(success ? "Info" : "Error", StatusMessage);
        }
        catch (Exception ex)
        {
            model.LastStatus = "Eject failed";
            StatusMessage = $"Failed to eject model: {model.Id}";
            AddLog("Error", ex.Message);
        }
        finally
        {
            model.IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task RunPromptAsync()
    {
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            AddLog("Warning", "Prompt is empty.");
            StatusMessage = "Enter a prompt first.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(DatasetRoot))
        {
            await RunDatasetPromptAsync();
            return;
        }

        var loadedModels = Models.Where(model => model.IsLoaded).ToList();

        if (loadedModels.Count == 0)
        {
            AddLog("Warning", "No loaded models available.");
            StatusMessage = "Load at least one model first.";
            PromptResponseTitle = "Prompt Response";
            ComparisonResults.Clear();
            return;
        }

        IsBusy = true;
        ResponseText = "";
        ComparisonResults.Clear();

        if (loadedModels.Count == 1)
        {
            PromptResponseTitle = "Single Model Response";

            var model = loadedModels[0];

            model.IsRunning = true;
            model.LastStatus = "Running";
            model.LastElapsedMilliseconds = 0;

            StatusMessage = $"Running prompt on {model.Id}";
            AddLog("Info", $"Sending prompt to loaded model: {model.Id}");

            var result = await _client.SendPromptAsync(BaseUrl, new PromptRequest
            {
                ModelId = model.Id,
                Prompt = Prompt,
                Temperature = 0.7,
                MaxTokens = 512
            });

            model.IsRunning = false;
            model.LastElapsedMilliseconds = result.ElapsedMilliseconds;

            if (result.Success)
            {
                model.LastStatus = "Loaded";

                ComparisonResults.Add(new MultiModelResult
                {
                    ModelId = model.Id,
                    Success = true,
                    ElapsedMilliseconds = result.ElapsedMilliseconds,
                    FullResponse = result.ResponseText,
                    ResponsePreview = result.ResponseText
                });

                StatusMessage = $"Completed in {result.ElapsedMilliseconds} ms.";
                AddLog("Info", $"{model.Id} completed in {result.ElapsedMilliseconds} ms.");
            }
            else
            {
                model.LastStatus = "Loaded - prompt failed";

                ComparisonResults.Add(new MultiModelResult
                {
                    ModelId = model.Id,
                    Success = false,
                    ElapsedMilliseconds = result.ElapsedMilliseconds,
                    FullResponse = result.ErrorMessage ?? "Unknown error.",
                    ErrorMessage = result.ErrorMessage ?? "Unknown error.",
                    ResponsePreview = "Failed"
                });

                StatusMessage = "Prompt failed.";
                AddLog("Error", result.ErrorMessage ?? "Unknown error.");
            }

            IsBusy = false;
            return;
        }

        PromptResponseTitle = "Multi-Model Response";

        StatusMessage = $"Running comparison across {loadedModels.Count} loaded models.";
        AddLog("Info", $"Running comparison across {loadedModels.Count} loaded models.");

        foreach (var model in loadedModels)
        {
            model.IsRunning = true;
            model.LastStatus = "Queued";
            model.LastElapsedMilliseconds = 0;
        }

        foreach (var model in loadedModels)
        {
            model.LastStatus = "Running";
            AddLog("Info", $"Sending prompt to loaded model: {model.Id}");

            var result = await _client.SendPromptAsync(BaseUrl, new PromptRequest
            {
                ModelId = model.Id,
                Prompt = Prompt,
                Temperature = 0.7,
                MaxTokens = 512
            });

            model.IsRunning = false;
            model.LastElapsedMilliseconds = result.ElapsedMilliseconds;

            if (result.Success)
            {
                model.LastStatus = "Loaded";

                ComparisonResults.Add(new MultiModelResult
                {
                    ModelId = model.Id,
                    Success = true,
                    ElapsedMilliseconds = result.ElapsedMilliseconds,
                    FullResponse = result.ResponseText,
                    ResponsePreview = result.ResponseText
                });

                AddLog("Info", $"{model.Id} completed in {result.ElapsedMilliseconds} ms.");
            }
            else
            {
                model.LastStatus = "Loaded - prompt failed";

                ComparisonResults.Add(new MultiModelResult
                {
                    ModelId = model.Id,
                    Success = false,
                    ElapsedMilliseconds = result.ElapsedMilliseconds,
                    FullResponse = result.ErrorMessage ?? "Unknown error.",
                    ErrorMessage = result.ErrorMessage ?? "Unknown error.",
                    ResponsePreview = "Failed"
                });

                AddLog("Error", $"{model.Id} failed: {result.ErrorMessage}");
            }
        }

        StatusMessage = "Prompt response complete.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task RunPromptOnAllModelsAsync()
    {
        if (Models.Count == 0)
        {
            AddLog("Warning", "No models available. Test the connection first.");
            StatusMessage = "No models available. Test the connection first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Prompt))
        {
            AddLog("Warning", "Prompt is empty.");
            StatusMessage = "Enter a prompt first.";
            return;
        }

        IsBusy = true;
        ComparisonResults.Clear();
        ResponseText = "";

        foreach (var model in Models)
        {
            model.IsRunning = true;
            model.LastStatus = "Queued";
            model.LastElapsedMilliseconds = 0;
        }

        StatusMessage = $"Running prompt on {Models.Count} model(s)... this may take a while.";
        AddLog("Info", $"Running prompt on {Models.Count} model(s).");

        foreach (var model in Models)
        {
            model.LastStatus = "Running";
            AddLog("Info", $"Sending prompt to {model.Id}");

            var request = new PromptRequest
            {
                ModelId = model.Id,
                Prompt = Prompt,
                Temperature = 0.7,
                MaxTokens = 512
            };

            var result = await _client.SendPromptAsync(BaseUrl, request);

            model.IsRunning = false;
            model.LastElapsedMilliseconds = result.ElapsedMilliseconds;

            if (result.Success)
            {
                model.LastStatus = "Complete";

                ComparisonResults.Add(new MultiModelResult
                {
                    ModelId = model.Id,
                    Success = true,
                    ElapsedMilliseconds = result.ElapsedMilliseconds,
                    FullResponse = result.ResponseText,
                    ResponsePreview = CreatePreview(result.ResponseText)
                });

                AddLog("Info", $"{model.Id} completed in {result.ElapsedMilliseconds} ms.");
            }
            else
            {
                model.LastStatus = "Failed";

                ComparisonResults.Add(new MultiModelResult
                {
                    ModelId = model.Id,
                    Success = false,
                    ElapsedMilliseconds = result.ElapsedMilliseconds,
                    ErrorMessage = result.ErrorMessage ?? "Unknown error.",
                    ResponsePreview = "Failed"
                });

                AddLog("Error", $"{model.Id} failed: {result.ErrorMessage}");
            }
        }

        StatusMessage = "Model comparison complete.";
        IsBusy = false;
    }

    [RelayCommand]
    private void SelectModel(MLModel model)
    {
        foreach (var existingModel in Models)
        {
            existingModel.IsSelected = false;
        }

        model.IsSelected = true;
        SelectedModel = model;
        SelectedModelId = model.Id;

        StatusMessage = $"Selected model: {model.Id}";
        AddLog("Info", $"Seeletced model: {model.Id}");
    }

    partial void OnSelectedModelChanged(MLModel? value)
    {
        foreach (var model in Models)
        {
            model.IsSelected = false;
        }

        if (value is not null)
        {
            value.IsSelected = true;
            SelectedModelId = value.Id;
            StatusMessage = $"Selected model: {value.Id}";
            AddLog("Info", $"Selected model: {value.Id}");
        }
    }

    private static string CreatePreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        const int maxLength = 180;

        return text.Length <= maxLength
            ? text
            : text.Substring(0, maxLength) + "...";
    }

    private static List<string> PickHouseImages(string objectDirectory, int limit)
    {
        var rgbDirectory = Path.Combine(objectDirectory, "rgb");

        if (!Directory.Exists(rgbDirectory))
        {
            return new List<string>();
        }

        var files = Directory
            .GetFiles(rgbDirectory)
            .Where(path =>
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var name = Path.GetFileName(path).ToLowerInvariant();

                return (ext is ".png" or ".jpg" or ".jpeg" or ".webp") &&
                    (name.StartsWith("view_") || ext is ".png" or ".jpg" or ".jpeg" or ".webp");
            })
            .OrderBy(path => path)
            .Take(limit)
            .ToList();

        return files;
    }

    private static async Task<string> EncodeImageAsDataUrlAsync(string imagePath)
    {
        var extension = Path.GetExtension(imagePath).ToLowerInvariant();

        var mimeType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/png"
        };

        var bytes = await File.ReadAllBytesAsync(imagePath);
        var base64 = Convert.ToBase64String(bytes);

        return $"data:{mimeType};base64,{base64}";
    }

    private async Task RunDatasetPromptAsync()
    {
        if (string.IsNullOrWhiteSpace(DatasetRoot) || !Directory.Exists(DatasetRoot))
        {
            StatusMessage = "Choose a valid dataset folder first.";
            PromptResponseTitle = "Prompt Response";
            AddLog("Warning", "Invalid dataset folder.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Prompt))
        {
            StatusMessage = "Enter captioning instructions in the prompt box first.";
            PromptResponseTitle = "Prompt Response";
            CaptionProgressText = "Prompt box is empty.";
            AddLog("Warning", "Dataset prompt was cancelled because the prompt box is empty.");
            return;
        }

        var loadedModels = Models.Where(model => model.IsLoaded).ToList();

        if (loadedModels.Count == 0)
        {
            StatusMessage = "Load a vision model first.";
            PromptResponseTitle = "Prompt Response";
            AddLog("Warning", "No loaded vision model available.");
            return;
        }

        var modelToUse = SelectedModel is not null && SelectedModel.IsLoaded
            ? SelectedModel
            : loadedModels[0];

        IsBusy = true;
        IsCaptioning = true;

        CaptionResults.Clear();
        ComparisonResults.Clear();

        PromptResponseTitle = "Dataset Captioning Response";

        modelToUse.IsRunning = true;
        modelToUse.LastStatus = "Captioning dataset";
        modelToUse.LastElapsedMilliseconds = 0;

        StatusMessage = $"Captioning dataset using {modelToUse.Id}.";
        CaptionProgressText = StatusMessage;
        AddLog("Info", $"Captioning dataset using {modelToUse.Id}.");

        try
        {
            var objectDirectories = Directory
                .GetDirectories(DatasetRoot)
                .Where(dir => Directory.Exists(Path.Combine(dir, "rgb")))
                .OrderBy(dir => dir)
                .ToList();

            if (objectDirectories.Count == 0)
            {
                CaptionProgressText = "No house folders with rgb/ were found.";
                StatusMessage = CaptionProgressText;

                ComparisonResults.Add(new MultiModelResult
                {
                    ModelId = "Dataset",
                    Success = false,
                    ElapsedMilliseconds = 0,
                    FullResponse = CaptionProgressText,
                    ErrorMessage = CaptionProgressText,
                    ResponsePreview = "No house folders found"
                });

                AddLog("Warning", CaptionProgressText);
                return;
            }

            var allCaptions = new Dictionary<string, string>();

            for (var i = 0; i < objectDirectories.Count; i++)
            {
                var objectDirectory = objectDirectories[i];
                var objectName = Path.GetFileName(objectDirectory);

                CaptionProgressText = $"Captioning {i + 1}/{objectDirectories.Count}: {objectName}";
                StatusMessage = CaptionProgressText;
                AddLog("Info", CaptionProgressText);

                var imagePaths = PickHouseImages(objectDirectory, MaxImagesPerHouse);

                if (imagePaths.Count == 0)
                {
                    var errorMessage = "No images found.";

                    CaptionResults.Add(new CaptionResult
                    {
                        ObjectName = objectName,
                        ModelId = modelToUse.Id,
                        Success = false,
                        ErrorMessage = errorMessage,
                        ImageCount = 0
                    });

                    ComparisonResults.Add(new MultiModelResult
                    {
                        ModelId = $"{objectName} — {modelToUse.Id}",
                        Success = false,
                        ElapsedMilliseconds = 0,
                        FullResponse = errorMessage,
                        ErrorMessage = errorMessage,
                        ResponsePreview = "Failed"
                    });

                    AddLog("Warning", $"Skipped {objectName}: {errorMessage}");
                    continue;
                }

                try
                {
                    var imageDataUrls = new List<string>();

                    foreach (var imagePath in imagePaths)
                    {
                        imageDataUrls.Add(await EncodeImageAsDataUrlAsync(imagePath));
                    }

                    var result = await _client.SendVisionPromptAsync(
                        BaseUrl,
                        modelToUse.Id,
                        "You are a helpful vision-language assistant.",
                        Prompt,
                        imageDataUrls);

                    modelToUse.LastElapsedMilliseconds = result.ElapsedMilliseconds;

                    if (!result.Success)
                    {
                        var errorMessage = result.ErrorMessage ?? "Unknown error.";

                        CaptionResults.Add(new CaptionResult
                        {
                            ObjectName = objectName,
                            ModelId = modelToUse.Id,
                            Success = false,
                            ErrorMessage = errorMessage,
                            ImageCount = imagePaths.Count
                        });

                        ComparisonResults.Add(new MultiModelResult
                        {
                            ModelId = $"{objectName} — {modelToUse.Id}",
                            Success = false,
                            ElapsedMilliseconds = result.ElapsedMilliseconds,
                            FullResponse = errorMessage,
                            ErrorMessage = errorMessage,
                            ResponsePreview = "Failed"
                        });

                        AddLog("Error", $"Failed captioning {objectName}: {errorMessage}");
                        continue;
                    }

                    var caption = result.ResponseText.Trim();

                    CaptionResults.Add(new CaptionResult
                    {
                        ObjectName = objectName,
                        ModelId = modelToUse.Id,
                        Caption = caption,
                        Success = true,
                        ImageCount = imagePaths.Count
                    });

                    ComparisonResults.Add(new MultiModelResult
                    {
                        ModelId = $"{objectName} — {modelToUse.Id}",
                        Success = true,
                        ElapsedMilliseconds = result.ElapsedMilliseconds,
                        FullResponse = caption,
                        ResponsePreview = caption
                    });

                    allCaptions[objectName] = caption;

                    await WriteCaptionToMetaJsonAsync(objectDirectory, caption);

                    AddLog("Info", $"Captioned {objectName}: {caption}");
                }
                catch (Exception ex)
                {
                    CaptionResults.Add(new CaptionResult
                    {
                        ObjectName = objectName,
                        ModelId = modelToUse.Id,
                        Success = false,
                        ErrorMessage = ex.Message,
                        ImageCount = imagePaths.Count
                    });

                    ComparisonResults.Add(new MultiModelResult
                    {
                        ModelId = $"{objectName} — {modelToUse.Id}",
                        Success = false,
                        ElapsedMilliseconds = 0,
                        FullResponse = ex.Message,
                        ErrorMessage = ex.Message,
                        ResponsePreview = "Failed"
                    });

                    AddLog("Error", $"Failed captioning {objectName}: {ex.Message}");
                }
            }

            var outputPath = Path.Combine(DatasetRoot, "captions_modelhub.json");

            await File.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(allCaptions, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

            CaptionProgressText = $"Done. Wrote: {outputPath}";
            StatusMessage = "Dataset captioning complete.";
            AddLog("Info", CaptionProgressText);
        }
        finally
        {
            modelToUse.IsRunning = false;
            modelToUse.LastStatus = "Loaded";
            IsCaptioning = false;
            IsBusy = false;
        }
    }


    private static async Task WriteCaptionToMetaJsonAsync(string objectDirectory, string caption)
    {
        var metaPath = Path.Combine(objectDirectory, "meta.json");

        Dictionary<string, object?> meta = new();

        if (File.Exists(metaPath))
        {
            try
            {
                var existingJson = await File.ReadAllTextAsync(metaPath);
                var existingMeta = JsonSerializer.Deserialize<Dictionary<string, object?>>(existingJson);

                if (existingMeta is not null)
                {
                    meta = existingMeta;
                }
            }
            catch
            {
                meta = new Dictionary<string, object?>();
            }
        }

        meta["caption"] = caption;

        var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(metaPath, json);
    }

    private void AddLog(string level, string message)
    {
        Logs.Insert(0, new AppLogEntry
        {
            Level = level,
            Message = message
        });
    }
}