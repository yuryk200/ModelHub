namespace ModelHub.Models;

public class PromptResult
{
    public string ModelId { get; set; } = "";
    public string ResponseText { get; set; } = "";
    public long ElapsedMilliseconds { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}