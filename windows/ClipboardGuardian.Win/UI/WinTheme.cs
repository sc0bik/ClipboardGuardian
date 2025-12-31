using System.Windows.Forms;

namespace ClipboardGuardian.Win.UI;

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

