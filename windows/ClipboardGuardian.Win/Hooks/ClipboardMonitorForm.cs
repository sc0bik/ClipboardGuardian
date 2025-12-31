using System.Windows.Forms;
using ClipboardGuardian.Win.Core.Models;
using ClipboardGuardian.Win.Services;

namespace ClipboardGuardian.Win.Hooks;

internal sealed class ClipboardMonitorForm : Form
{
    private const int WmClipboardUpdate = 0x031D;
    private const uint WmUserDecision = 0x0400 + 1;
    private readonly Action<ClipboardSnapshot> _onChange;
    private long _suppressUntil;

    public event Action? PasteRequested;

    public ClipboardMonitorForm(Action<ClipboardSnapshot> onChange)
    {
        _onChange = onChange;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        WindowState = FormWindowState.Minimized;
        Load += (_, _) => NativeMethods.AddClipboardFormatListener(Handle);
        FormClosed += (_, _) => NativeMethods.RemoveClipboardFormatListener(Handle);
    }

    public void SuppressFor(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        if (until > _suppressUntil) _suppressUntil = until;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmClipboardUpdate)
        {
            if (Environment.TickCount64 < _suppressUntil)
            {
                base.WndProc(ref m);
                return;
            }

            BeginInvoke(() =>
            {
                if (Environment.TickCount64 < _suppressUntil) return;
                var snapshot = ClipboardUtilities.TryCaptureSnapshotWithRetry();
                _onChange(snapshot);
            });
        }
        else if (m.Msg == WmUserDecision)
        {
            PasteRequested?.Invoke();
        }

        base.WndProc(ref m);
    }
}
