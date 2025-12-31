using System.Linq;
using System.Text;
using System.Windows.Forms;
using ClipboardGuardian.Win.Core.Interfaces;
using ClipboardGuardian.Win.Core.Models;
using ClipboardGuardian.Win.Logging;
using ClipboardGuardian.Win.UI;
using ClipboardLogEntry = ClipboardGuardian.Win.Core.Models.ClipboardLogEntry;

namespace ClipboardGuardian.Win.UI;

internal sealed class MainForm : Form
{
    private const int HistoryMaxEntries = 200;
    private readonly Func<bool> _getProtectionEnabled;
    private readonly Action<bool> _setProtectionEnabled;
    private readonly IClipboardLogReader _logReader;
    private readonly Action _exitApp;
    private readonly Label _statusLabel;
    private readonly Button _toggleButton;
    private readonly TextBox _historyBox;
    private readonly Button _refreshHistoryButton;
    private bool _allowClose;

    public MainForm(
        Func<bool> getProtectionEnabled,
        Action<bool> setProtectionEnabled,
        IClipboardLogReader logReader,
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
            Text = "Защdита показыывает подтверждение при копировании и вставке текста.",
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

