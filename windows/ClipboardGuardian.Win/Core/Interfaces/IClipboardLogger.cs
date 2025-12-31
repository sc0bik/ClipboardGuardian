namespace ClipboardGuardian.Win.Core.Interfaces;

internal interface IClipboardLogger
{
    void Log(string actionType, string decision, string? sample, string note);
}

internal interface IClipboardLogReader
{
    IReadOnlyList<Core.Models.ClipboardLogEntry> ReadRecentEntries(int maxEntries);
}

