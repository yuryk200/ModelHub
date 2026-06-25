using System;

namespace ModelHub.Models;

public class AppLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = "";

    public override string ToString()
    {
        return $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
    }
}