using System.Threading;
using System.Windows.Forms;
using ClipboardGuardian.Win.Core.Interfaces;
using ClipboardGuardian.Win.Core.Models;
using ClipboardGuardian.Win.Hooks;
using ClipboardGuardian.Win.Logging;
using ClipboardGuardian.Win.Services;
using ClipboardGuardian.Win.UI;

namespace ClipboardGuardian.Win.Services;

internal sealed class ClipboardAgent : ApplicationContext
{
    private const int SuppressMs = 900;
    private readonly ClipboardMonitorForm _monitor;
    private readonly KeyboardHook _keyboardHook;
    private readonly IClipboardLogger _logger;
    private readonly IClipboardLogReader _logReader;
    private readonly NotifyIcon _tray;
    private readonly SynchronizationContext _uiCtx;
    private readonly MainForm _mainForm;
    private ClipboardSnapshot? _lastSnapshot;
    private bool _enabled = true;
    private bool _checkingRead = false;

    public ClipboardAgent()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "clipboard_log.ndjson");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        _logger = new ClipboardLogWriter(logPath);
        _logReader = new ClipboardLogReader(logPath);
        _uiCtx = SynchronizationContext.Current ?? new SynchronizationContext();

        _monitor = new ClipboardMonitorForm(OnClipboardChange);
        _keyboardHook = new KeyboardHook();
        _keyboardHook.IsProtectionEnabled = () => _enabled;
        _keyboardHook.PasteRequested += () => _uiCtx.Post(_ => OnReadAttempt(), null);
        _keyboardHook.Start();

        _mainForm = new MainForm(
            getProtectionEnabled: () => _enabled,
            setProtectionEnabled: enabled =>
            {
                _enabled = enabled;
                _logger.Log("toggle", enabled ? "enabled" : "disabled", null, "Protection toggled by user");
            },
            logReader: _logReader,
            exitApp: () =>
            {
                ExitThread();
            }
        );

        _tray = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Visible = true,
            Text = "Clipboard Guardian"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _mainForm.AllowClose();
            ExitThread();
        });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowWindow();

        _monitor.Show();
        ShowWindow();
    }

    protected override void ExitThreadCore()
    {
        _mainForm.AllowClose();
        _keyboardHook.Dispose();
        _monitor.Dispose();
        _mainForm.Dispose();
        _tray?.Dispose();
        base.ExitThreadCore();
    }

    private void OnClipboardChange(ClipboardSnapshot snapshot)
    {
        if (!_enabled) return;
        if (snapshot.Kind == ClipboardKind.Empty) return;

        var canSave = snapshot.Kind is ClipboardKind.Text or ClipboardKind.Files;
        if (canSave)
        {
            SuppressEvents();
            ClipboardUtilities.TryClear();
        }

        var decision = AccessPromptForm.AskUser(
            "Обнаружено копирование в буфер обмена. Разрешить сохранить данные?",
            snapshot.Preview
        );
        if (decision == AccessDecision.Allow)
        {
            if (canSave)
            {
                SuppressEvents();
                ClipboardUtilities.TryRestoreSnapshot(snapshot);
                _lastSnapshot = snapshot;
            }

            _logger.Log("copy", "allowed", snapshot.Preview, "Clipboard updated by user");
            return;
        }

        SuppressEvents();
        if (_lastSnapshot is not null)
        {
            ClipboardUtilities.TryRestoreSnapshot(_lastSnapshot);
        }
        else
        {
            ClipboardUtilities.TryClear();
        }

        _logger.Log("copy", "blocked", snapshot.Preview, "Clipboard change reverted");
    }

    private void OnReadAttempt()
    {
        if (!_enabled || _checkingRead) return;
        _checkingRead = true;

        if (_monitor.InvokeRequired)
        {
            _monitor.Invoke(new Action(OnReadAttemptInternal));
            return;
        }

        OnReadAttemptInternal();
    }

    private void OnReadAttemptInternal()
    {
        var snapshot = ClipboardUtilities.TryCaptureSnapshotWithRetry();
        var preview = snapshot;
        if ((snapshot.Kind == ClipboardKind.Empty || snapshot.Kind == ClipboardKind.Unsupported) && _lastSnapshot is not null)
        {
            preview = _lastSnapshot with
            {
                Preview = _lastSnapshot.Preview + "\r\n\r\n(показано последнее разрешённое содержимое)"
            };
        }
        
        var decision = AccessPromptForm.AskUser(
            "Приложение пытается вставить данные из буфера обмена. Разрешить вставку?",
            preview.Preview
        );
        
        if (decision == AccessDecision.Allow)
        {
            if ((snapshot.Kind == ClipboardKind.Empty || snapshot.Kind == ClipboardKind.Unsupported) && _lastSnapshot is not null)
            {
                SuppressEvents();
                ClipboardUtilities.TryRestoreSnapshot(_lastSnapshot);
            }
            _keyboardHook.SimulatePaste();
            _logger.Log("paste", "allowed", preview.Preview, "Paste permitted by user");
        }
        else
        {
            _logger.Log("paste", "blocked", preview.Preview, "Paste denied by user");
        }
        
        _checkingRead = false;
    }

    private void SuppressEvents()
    {
        _monitor.SuppressFor(SuppressMs);
    }

    private void ShowWindow()
    {
        if (_mainForm.Visible)
        {
            _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Activate();
            _mainForm.RefreshHistory();
            return;
        }

        _mainForm.Show();
        _mainForm.RefreshHistory();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }
}
