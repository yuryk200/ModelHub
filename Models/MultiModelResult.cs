namespace ModelHub.Models;

public class MultiModelResult
{
    public string ModelId { get; set; } = "";
    public bool Success { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public string ResponsePreview { get; set; } = "";
    public string FullResponse { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
}