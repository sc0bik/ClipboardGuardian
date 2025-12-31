using System.Text;
using System.Text.Json;
using ClipboardGuardian.Win.Core.Interfaces;
using ClipboardLogEntry = ClipboardGuardian.Win.Core.Models.ClipboardLogEntry;

namespace ClipboardGuardian.Win.Logging;

internal sealed class ClipboardLogReader : IClipboardLogReader
{
    private readonly string _path;

    public ClipboardLogReader(string path)
    {
        _path = path;
    }

    public IReadOnlyList<ClipboardLogEntry> ReadRecentEntries(int maxEntries)
    {
        if (maxEntries <= 0) return Array.Empty<ClipboardLogEntry>();
        if (!File.Exists(_path)) return Array.Empty<ClipboardLogEntry>();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var entries = new Queue<ClipboardLogEntry>(maxEntries);
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<ClipboardLogEntry>(line, options);
                    if (entry is null) continue;
                    entries.Enqueue(entry);
                    if (entries.Count > maxEntries) entries.Dequeue();
                }
                catch
                {
                }
            }
        }
        catch
        {
            return Array.Empty<ClipboardLogEntry>();
        }

        return entries.ToArray();
    }
}
