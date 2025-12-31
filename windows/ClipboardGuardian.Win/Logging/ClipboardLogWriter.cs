using System.Text.Json;
using ClipboardGuardian.Win.Core.Interfaces;
using ClipboardLogEntry = ClipboardGuardian.Win.Core.Models.ClipboardLogEntry;

namespace ClipboardGuardian.Win.Logging;

internal sealed class ClipboardLogWriter : IClipboardLogger
{
    private readonly object _sync = new();
    private readonly string _path;

    public ClipboardLogWriter(string path)
    {
        _path = path;
    }

    public void Log(string actionType, string decision, string? sample, string note)
    {
        var entry = new ClipboardLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Action = actionType,
            Decision = decision,
            Sample = string.IsNullOrWhiteSpace(sample) ? null : Truncate(sample!, 200),
            Note = note
        };

        var json = JsonSerializer.Serialize(entry);
        lock (_sync)
        {
            File.AppendAllText(_path, json + Environment.NewLine);
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
    }
}

