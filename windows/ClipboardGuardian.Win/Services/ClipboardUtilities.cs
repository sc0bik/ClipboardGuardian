using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ClipboardGuardian.Win.Core.Models;

namespace ClipboardGuardian.Win.Services;

internal static class ClipboardUtilities
{
    private const int DefaultPreviewMaxChars = 4000;
    private const int ClipboardRetryAttempts = 20;
    private const int ClipboardRetryDelayMs = 25;

    public static string? TryGetText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            return null;
        }
    }

    public static ClipboardSnapshot TryCaptureSnapshotWithRetry(int attempts = 10, int delayMs = 25)
    {
        for (var i = 0; i < attempts; i++)
        {
            var snapshot = TryCaptureSnapshotOnce();
            if (snapshot.Kind != ClipboardKind.Unsupported) return snapshot;
            Thread.Sleep(delayMs);
        }

        return TryCaptureSnapshotOnce();
    }

    private static ClipboardSnapshot TryCaptureSnapshotOnce()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
                if (files.Length == 0) return new ClipboardSnapshot(ClipboardKind.Empty, null, null, "Пусто");

                var preview = "Файлы:\r\n" + string.Join(
                    "\r\n",
                    files.Take(20).Select(f => "• " + Path.GetFileName(f))
                );
                if (files.Length > 20) preview += $"\r\n…и ещё {files.Length - 20}";
                return new ClipboardSnapshot(ClipboardKind.Files, null, files, preview);
            }

            if (Clipboard.ContainsText(TextDataFormat.UnicodeText) || Clipboard.ContainsText())
            {
                var text = Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    ? Clipboard.GetText(TextDataFormat.UnicodeText)
                    : Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return new ClipboardSnapshot(ClipboardKind.Empty, null, null, "Пусто");
                if (text.Length > DefaultPreviewMaxChars) text = text[..DefaultPreviewMaxChars] + "…";
                return new ClipboardSnapshot(ClipboardKind.Text, text, null, text);
            }

            return new ClipboardSnapshot(ClipboardKind.Unsupported, null, null, "Нет поддерживаемого содержимого (не текст/не файлы).");
        }
        catch
        {
            return new ClipboardSnapshot(ClipboardKind.Unsupported, null, null, "Не удалось прочитать буфер (занят другим приложением).");
        }
    }

    public static void TryRestoreSnapshot(ClipboardSnapshot snapshot)
    {
        WithClipboardRetry(() =>
        {
            switch (snapshot.Kind)
            {
                case ClipboardKind.Text:
                    if (!string.IsNullOrEmpty(snapshot.Text)) Clipboard.SetText(snapshot.Text, TextDataFormat.UnicodeText);
                    else Clipboard.Clear();
                    break;
                case ClipboardKind.Files:
                    if (snapshot.Files is { Length: > 0 })
                    {
                        var list = new StringCollection();
                        list.AddRange(snapshot.Files);
                        Clipboard.SetFileDropList(list);
                    }
                    else
                    {
                        Clipboard.Clear();
                    }
                    break;
                default:
                    Clipboard.Clear();
                    break;
            }
        });
    }

    public static void TrySetText(string text)
    {
        WithClipboardRetry(() => Clipboard.SetText(text, TextDataFormat.UnicodeText));
    }

    public static void TryClear()
    {
        WithClipboardRetry(() => Clipboard.Clear());
    }

    private static void WithClipboardRetry(Action action)
    {
        for (var i = 0; i < ClipboardRetryAttempts; i++)
        {
            try
            {
                action();
                return;
            }
            catch
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
        }
    }
}

