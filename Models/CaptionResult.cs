namespace ModelHub.Models;

public class CaptionResult
{
    public string ObjectName { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string Caption { get; set; } = "";
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = "";
    public int ImageCount { get; set; }
}