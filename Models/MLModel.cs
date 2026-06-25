using CommunityToolkit.Mvvm.ComponentModel;

namespace ModelHub.Models;

public partial class MLModel : ObservableObject
{
    public string Id { get; set; } = "";
    public string OwnedBy { get; set; } = "";
    public string InstanceId { get; set; } = "";

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private bool isLoaded;

    [ObservableProperty]
    private string lastStatus = "Ejected";

    [ObservableProperty]
    private long lastElapsedMilliseconds;

    public bool IsNotLoaded => !IsLoaded;

    partial void OnIsLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotLoaded));
    }
}