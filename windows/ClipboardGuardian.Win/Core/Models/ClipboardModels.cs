namespace ClipboardGuardian.Win.Core.Models;

internal enum ClipboardKind
{
    Text,
    Files,
    Empty,
    Unsupported
}

internal sealed record ClipboardSnapshot(
    ClipboardKind Kind,
    string? Text,
    string[]? Files,
    string Preview
);

internal enum AccessDecision
{
    Allow,
    Deny
}

internal sealed class ClipboardLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string? Sample { get; set; }
    public string Note { get; set; } = string.Empty;
}

