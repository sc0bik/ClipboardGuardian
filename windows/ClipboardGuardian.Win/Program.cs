using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace ClipboardGuardian.Win;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var agent = new ClipboardAgent();
        Application.Run(agent);
    }
}

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

internal sealed class ClipboardAgent : ApplicationContext
{
    private const int SuppressClipboardMs = 900;
    private readonly ClipboardMonitorForm _monitorForm;
    private readonly KeyboardHook _keyboardHook;
    private readonly ClipboardLogWriter _logger;
    private readonly ClipboardLogReader _logReader;
    private readonly NotifyIcon _trayIcon;
    private readonly SynchronizationContext _uiContext;
    private readonly MainForm _mainForm;
    private ClipboardSnapshot? _lastApprovedSnapshot;
    private bool _protectionEnabled = true;

    public ClipboardAgent()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "clipboard_log.ndjson");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        _logger = new ClipboardLogWriter(logPath);
        _logReader = new ClipboardLogReader(logPath);
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

        _monitorForm = new ClipboardMonitorForm(HandleClipboardChanged);
        _keyboardHook = new KeyboardHook();
        _keyboardHook.IsProtectionEnabled = () => _protectionEnabled;
        _keyboardHook.PasteRequested += () => _uiContext.Post(_ => HandlePasteAttempt(), null);
        _keyboardHook.Start();

        _mainForm = new MainForm(
            getProtectionEnabled: () => _protectionEnabled,
            setProtectionEnabled: enabled =>
            {
                _protectionEnabled = enabled;
                _logger.Log("toggle", enabled ? "enabled" : "disabled", null, "Protection toggled by user");
            },
            logReader: _logReader,
            exitApp: () =>
            {
                ExitThread();
            }
        );

        // Set up a tray icon so the user can exit the agent.
        var tray = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Visible = true,
            Text = "Clipboard Guardian"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _mainForm.AllowClose();
            ExitThread();
        });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowMainWindow();
        _trayIcon = tray;

        _monitorForm.Show();
        ShowMainWindow();
    }

    protected override void ExitThreadCore()
    {
        _mainForm.AllowClose();
        _keyboardHook.Dispose();
        _monitorForm.Dispose();
        _mainForm.Dispose();
        _trayIcon?.Dispose();
        base.ExitThreadCore();
    }

    private void HandleClipboardChanged(string? textSample)
    {
        // Legacy signature kept for compatibility with older build outputs; unused.
    }

    private void HandleClipboardChanged(ClipboardSnapshot snapshot)
    {
        if (!_protectionEnabled) return;
        if (snapshot.Kind == ClipboardKind.Empty) return;

        // For supported types we can neutralize immediately so that other apps can't paste until user approves.
        var canRestore = snapshot.Kind is ClipboardKind.Text or ClipboardKind.Files;
        if (canRestore)
        {
            SuppressClipboardEvents();
            ClipboardUtilities.TryClear();
        }

        var decision = AccessPromptForm.AskUser(
            "Обнаружено копирование в буфер обмена. Разрешить сохранить данные?",
            snapshot.Preview
        );
        if (decision == AccessDecision.Allow)
        {
            if (canRestore)
            {
                SuppressClipboardEvents();
                ClipboardUtilities.TryRestoreSnapshot(snapshot);
                _lastApprovedSnapshot = snapshot;
            }

            _logger.Log("copy", "allowed", snapshot.Preview, "Clipboard updated by user");
            return;
        }

        SuppressClipboardEvents();
        if (_lastApprovedSnapshot is not null)
        {
            ClipboardUtilities.TryRestoreSnapshot(_lastApprovedSnapshot);
        }
        else
        {
            ClipboardUtilities.TryClear();
        }

        _logger.Log("copy", "blocked", snapshot.Preview, "Clipboard change reverted");
    }

    private void HandlePasteAttempt()
    {
        if (!_protectionEnabled) return;

        var snapshot = ClipboardUtilities.TryCaptureSnapshotWithRetry();
        var effective = snapshot;
        if ((snapshot.Kind == ClipboardKind.Empty || snapshot.Kind == ClipboardKind.Unsupported) && _lastApprovedSnapshot is not null)
        {
            effective = _lastApprovedSnapshot with
            {
                Preview = _lastApprovedSnapshot.Preview + "\r\n\r\n(показано последнее разрешённое содержимое)"
            };
        }
        var decision = AccessPromptForm.AskUser(
            "Приложение пытается вставить данные из буфера обмена. Разрешить вставку?",
            effective.Preview
        );
        if (decision == AccessDecision.Allow)
        {
            // If clipboard is currently empty/unsupported but we have an approved snapshot, restore it before pasting.
            if ((snapshot.Kind == ClipboardKind.Empty || snapshot.Kind == ClipboardKind.Unsupported) && _lastApprovedSnapshot is not null)
            {
                SuppressClipboardEvents();
                ClipboardUtilities.TryRestoreSnapshot(_lastApprovedSnapshot);
            }

            _keyboardHook.SimulatePaste();
            _logger.Log("paste", "allowed", effective.Preview, "Paste permitted by user (hotkey released after approval)");
            return;
        }

        // Paste hotkey is swallowed in the low-level hook, so deny = do nothing.
        _logger.Log("paste", "blocked", effective.Preview, "Paste denied by user");
    }

    private void SuppressClipboardEvents()
    {
        _monitorForm.SuppressFor(SuppressClipboardMs);
    }

    private void ShowMainWindow()
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

internal sealed class ClipboardMonitorForm : Form
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly Action<ClipboardSnapshot> _onClipboardChanged;
    private long _suppressUntilTick;

    public ClipboardMonitorForm(Action<ClipboardSnapshot> onClipboardChanged)
    {
        _onClipboardChanged = onClipboardChanged;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        WindowState = FormWindowState.Minimized;
        Load += (_, _) => NativeMethods.AddClipboardFormatListener(Handle);
        FormClosed += (_, _) => NativeMethods.RemoveClipboardFormatListener(Handle);
    }

    public void SuppressFor(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        if (until > _suppressUntilTick) _suppressUntilTick = until;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmClipboardUpdate)
        {
            if (Environment.TickCount64 < _suppressUntilTick)
            {
                base.WndProc(ref m);
                return;
            }

            // Reading clipboard inside WndProc is flaky (clipboard might still be "busy").
            // Defer a tiny bit onto the UI loop, then read with retry.
            BeginInvoke(() =>
            {
                if (Environment.TickCount64 < _suppressUntilTick) return;
                var snapshot = ClipboardUtilities.TryCaptureSnapshotWithRetry();
                _onClipboardChanged(snapshot);
            });
        }

        base.WndProc(ref m);
    }
}

internal sealed class MainForm : Form
{
    private const int HistoryMaxEntries = 200;
    private readonly Func<bool> _getProtectionEnabled;
    private readonly Action<bool> _setProtectionEnabled;
    private readonly ClipboardLogReader _logReader;
    private readonly Action _exitApp;
    private readonly Label _statusLabel;
    private readonly Button _toggleButton;
    private readonly TextBox _historyBox;
    private readonly Button _refreshHistoryButton;
    private bool _allowClose;

    public MainForm(
        Func<bool> getProtectionEnabled,
        Action<bool> setProtectionEnabled,
        ClipboardLogReader logReader,
        Action exitApp
    )
    {
        _getProtectionEnabled = getProtectionEnabled;
        _setProtectionEnabled = setProtectionEnabled;
        _logReader = logReader;
        _exitApp = exitApp;

        Text = "Clipboard Guardian";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new System.Drawing.Size(520, 480);

        var colors = WinTheme.Colors;
        BackColor = colors.Background;
        ForeColor = colors.OnBackground;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 6,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Clipboard Guardian",
            AutoSize = true,
            Font = new System.Drawing.Font(Font.FontFamily, 20, FontStyle.Bold),
            ForeColor = colors.OnBackground
        };
        root.Controls.Add(title, 0, 0);

        var desc = new Label
        {
            Text = "Защита показывает подтверждение при копировании и вставке текста.",
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(900, 0),
            ForeColor = colors.OnBackgroundMuted
        };
        root.Controls.Add(desc, 0, 1);

        var statusCard = WinTheme.MakeCard();
        statusCard.Dock = DockStyle.Top;
        statusCard.Padding = new Padding(16);
        var statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _statusLabel = new Label
        {
            AutoSize = true,
            Font = new System.Drawing.Font(Font.FontFamily, 12, FontStyle.Bold),
            ForeColor = colors.OnSurface
        };
        statusLayout.Controls.Add(_statusLabel, 0, 0);

        var hint = new Label
        {
            Text = "Примечание: приложение контролирует текст и копирование файлов. Для других форматов может показываться краткий предпросмотр.",
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(900, 0),
            ForeColor = colors.OnSurfaceMuted
        };
        statusLayout.Controls.Add(hint, 0, 1);
        statusCard.Controls.Add(statusLayout);
        root.Controls.Add(statusCard, 0, 2);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            Margin = new Padding(0, 16, 0, 0)
        };

        _toggleButton = new Button
        {
            Width = 200,
            Height = 44,
            FlatStyle = FlatStyle.Flat
        };
        _toggleButton.FlatAppearance.BorderSize = 0;
        _toggleButton.Click += (_, _) =>
        {
            _setProtectionEnabled(!_getProtectionEnabled());
            SetProtectionEnabled(_getProtectionEnabled());
        };
        buttonRow.Controls.Add(_toggleButton);

        root.Controls.Add(buttonRow, 0, 3);

        var historyCard = WinTheme.MakeCard();
        historyCard.Dock = DockStyle.Fill;
        historyCard.Padding = new Padding(16);
        var historyLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        historyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        historyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var historyHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true
        };
        historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var historyTitle = new Label
        {
            Text = "История обращений",
            AutoSize = true,
            Font = new System.Drawing.Font(Font.FontFamily, 12, FontStyle.Bold),
            ForeColor = colors.OnSurface
        };
        historyHeader.Controls.Add(historyTitle, 0, 0);

        _refreshHistoryButton = new Button
        {
            Text = "Обновить",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = colors.Surface,
            ForeColor = colors.OnSurface
        };
        _refreshHistoryButton.FlatAppearance.BorderColor = colors.Outline;
        _refreshHistoryButton.FlatAppearance.BorderSize = 1;
        _refreshHistoryButton.Click += (_, _) => RefreshHistory();
        historyHeader.Controls.Add(_refreshHistoryButton, 1, 0);

        historyLayout.Controls.Add(historyHeader, 0, 0);

        _historyBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = colors.Surface,
            ForeColor = colors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle
        };
        historyLayout.Controls.Add(_historyBox, 0, 1);
        historyCard.Controls.Add(historyLayout);
        root.Controls.Add(historyCard, 0, 4);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Margin = new Padding(0, 16, 0, 0)
        };
        var exit = new Button
        {
            Text = "Выйти",
            Width = 120,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = colors.Surface,
            ForeColor = colors.OnSurface
        };
        exit.FlatAppearance.BorderColor = colors.Outline;
        exit.FlatAppearance.BorderSize = 1;
        exit.Click += (_, _) =>
        {
            AllowClose();
            _exitApp();
        };
        footer.Controls.Add(exit);
        root.Controls.Add(footer, 0, 5);

        Controls.Add(root);
        SetProtectionEnabled(_getProtectionEnabled());
        RefreshHistory();
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    public void SetProtectionEnabled(bool enabled)
    {
        var colors = WinTheme.Colors;
        _statusLabel.Text = enabled ? "Статус: защита активна" : "Статус: защита остановлена";

        _toggleButton.Text = enabled ? "Остановить защиту" : "Запустить защиту";
        _toggleButton.BackColor = enabled ? colors.Primary : colors.Surface;
        _toggleButton.ForeColor = enabled ? colors.OnPrimary : colors.OnSurface;
    }

    public void RefreshHistory()
    {
        var entries = _logReader.ReadRecentEntries(HistoryMaxEntries)
            .Where(entry => entry.Action is "copy" or "paste")
            .Where(entry => entry.Decision is "allowed" or "blocked")
            .ToArray();

        if (entries.Length == 0)
        {
            _historyBox.Text = "История пока пуста.";
            return;
        }

        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.AppendLine(FormatHistoryEntry(entry));
        }

        _historyBox.Text = builder.ToString();
        _historyBox.SelectionStart = _historyBox.TextLength;
        _historyBox.ScrollToCaret();
    }

    private static string FormatHistoryEntry(ClipboardLogEntry entry)
    {
        var time = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        var sample = string.IsNullOrWhiteSpace(entry.Sample) ? "Пусто" : NormalizeSample(entry.Sample!);
        var decision = entry.Decision == "allowed" ? "разрешил" : "запретил";
        var action = entry.Action == "copy" ? "копирование" : "вставка";
        return $"{time} {sample} {action} ответ - {decision}";
    }

    private static string NormalizeSample(string value, int maxLength = 160)
    {
        var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length > maxLength) normalized = normalized[..maxLength] + "…";
        return normalized;
    }
}

internal sealed class AccessPromptForm : Form
{
    private readonly Label _messageLabel;
    private readonly TextBox _sampleBox;
    private readonly Button _allowButton;
    private readonly Button _denyButton;
    private AccessDecision _decision = AccessDecision.Deny;

    private AccessPromptForm(string message, string? sample)
    {
        Text = "Clipboard Guardian";
        Width = 520;
        Height = 340;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = WinTheme.Colors.Background;
        ForeColor = WinTheme.Colors.OnBackground;

        _messageLabel = new Label
        {
            Text = message,
            Dock = DockStyle.Top,
            Height = 76,
            Padding = new Padding(10),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = WinTheme.Colors.OnBackground
        };

        _sampleBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(sample) ? "Нет текстового содержимого" : sample,
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = WinTheme.Colors.Surface,
            ForeColor = WinTheme.Colors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 50,
            Padding = new Padding(10)
        };

        _allowButton = new Button
        {
            Text = "Разрешить",
            Width = 140,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = WinTheme.Colors.Primary,
            ForeColor = WinTheme.Colors.OnPrimary
        };
        _allowButton.FlatAppearance.BorderSize = 0;

        _denyButton = new Button
        {
            Text = "Запретить",
            Width = 140,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = WinTheme.Colors.Surface,
            ForeColor = WinTheme.Colors.OnSurface
        };
        _denyButton.FlatAppearance.BorderColor = WinTheme.Colors.Outline;
        _denyButton.FlatAppearance.BorderSize = 1;

        _allowButton.Click += (_, _) => { _decision = AccessDecision.Allow; Close(); };
        _denyButton.Click += (_, _) => { _decision = AccessDecision.Deny; Close(); };

        buttonPanel.Controls.Add(_allowButton);
        buttonPanel.Controls.Add(_denyButton);

        Controls.Add(_sampleBox);
        Controls.Add(buttonPanel);
        Controls.Add(_messageLabel);
    }

    public static AccessDecision AskUser(string message, string? sample)
    {
        using var form = new AccessPromptForm(message, sample);
        form.TopMost = true;
        form.ShowDialog();
        return form._decision;
    }
}

internal enum AccessDecision
{
    Allow,
    Deny
}

internal sealed class ClipboardLogWriter
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

internal sealed class ClipboardLogReader
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
                    var entry = JsonSerializer.Deserialize<ClipboardLogEntry>(line);
                    if (entry is null) continue;
                    entries.Enqueue(entry);
                    if (entries.Count > maxEntries) entries.Dequeue();
                }
                catch
                {
                    // skip malformed lines
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

internal sealed class ClipboardLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string? Sample { get; set; }
    public string Note { get; set; } = string.Empty;
}

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

internal static class WinTheme
{
    public static ThemeColors Colors { get; } = new ThemeColors(
        Background: System.Drawing.Color.FromArgb(0x12, 0x12, 0x12),
        OnBackground: System.Drawing.Color.FromArgb(0xF5, 0xF5, 0xF5),
        OnBackgroundMuted: System.Drawing.Color.FromArgb(0xB0, 0xB0, 0xB0),
        Surface: System.Drawing.Color.FromArgb(0x1E, 0x1E, 0x1E),
        OnSurface: System.Drawing.Color.FromArgb(0xF5, 0xF5, 0xF5),
        OnSurfaceMuted: System.Drawing.Color.FromArgb(0xB0, 0xB0, 0xB0),
        Primary: System.Drawing.Color.FromArgb(0x7C, 0xA7, 0xFF),
        OnPrimary: System.Drawing.Color.FromArgb(0x10, 0x10, 0x10),
        Outline: System.Drawing.Color.FromArgb(0x3A, 0x3A, 0x3A)
    );

    public static Panel MakeCard()
    {
        return new Panel
        {
            BackColor = Colors.Surface,
            ForeColor = Colors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 16, 0, 0)
        };
    }

    public sealed record ThemeColors(
        System.Drawing.Color Background,
        System.Drawing.Color OnBackground,
        System.Drawing.Color OnBackgroundMuted,
        System.Drawing.Color Surface,
        System.Drawing.Color OnSurface,
        System.Drawing.Color OnSurfaceMuted,
        System.Drawing.Color Primary,
        System.Drawing.Color OnPrimary,
        System.Drawing.Color Outline
    );
}

internal sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int VkInsert = 0x2D;
    private const int VkV = 0x56;

    private nint _hookId = nint.Zero;
    private NativeMethods.HookProc? _proc;
    private volatile int _suppressNextPaste;

    public Func<bool>? IsProtectionEnabled { get; set; }

    public event Action? PasteRequested;

    public void Start()
    {
        _proc = HookCallback;
        _hookId = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _proc, NativeMethods.GetModuleHandle(nint.Zero), 0);
    }

    public void Dispose()
    {
        if (_hookId != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = nint.Zero;
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && wParam == (nint)WmKeydown)
        {
            var hookStruct = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var isCtrlPressed = (NativeMethods.GetKeyState((int)Keys.LControlKey) & 0x8000) != 0 ||
                                (NativeMethods.GetKeyState((int)Keys.RControlKey) & 0x8000) != 0;
            var isShiftPressed = (NativeMethods.GetKeyState((int)Keys.LShiftKey) & 0x8000) != 0 ||
                                 (NativeMethods.GetKeyState((int)Keys.RShiftKey) & 0x8000) != 0;

            var isPasteHotkey = (hookStruct.vkCode == VkV && isCtrlPressed) ||
                                (hookStruct.vkCode == VkInsert && isShiftPressed);

            if (isPasteHotkey)
            {
                if (_suppressNextPaste > 0)
                {
                    _suppressNextPaste = 0;
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                if (IsProtectionEnabled?.Invoke() == true)
                {
                    PasteRequested?.Invoke();
                    return (nint)1; // swallow hotkey until user approves
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void SimulatePaste()
    {
        try
        {
            _suppressNextPaste = 1;
            NativeMethods.SendCtrlV();
        }
        catch
        {
            // ignore
        }
    }
}

internal static class NativeMethods
{
    public delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool RemoveClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    public static extern nint GetModuleHandle(nint lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    private const int InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkVKey = 0x56;

    public static void SendCtrlV()
    {
        var inputs = new[]
        {
            Input.KeyDown(VkControl),
            Input.KeyDown(VkVKey),
            Input.KeyUp(VkVKey),
            Input.KeyUp(VkControl),
        };

        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int type;
        public InputUnion u;

        public static Input KeyDown(ushort vk) => new Input
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = nint.Zero
                }
            }
        };

        public static Input KeyUp(ushort vk) => new Input
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = KeyeventfKeyup,
                    time = 0,
                    dwExtraInfo = nint.Zero
                }
            }
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct KbdLlHookStruct
{
    public int vkCode;
    public int scanCode;
    public int flags;
    public int time;
    public nint dwExtraInfo;
}
