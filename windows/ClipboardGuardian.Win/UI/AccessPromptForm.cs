using System.Windows.Forms;
using ClipboardGuardian.Win.Core.Models;
using ClipboardGuardian.Win.UI;

namespace ClipboardGuardian.Win.UI;

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

