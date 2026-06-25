namespace ModelHub.Models;

public class PromptRequest
{
    public string ModelId { get; set; } = "";
    public string Prompt { get; set; } = "";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 512;
}