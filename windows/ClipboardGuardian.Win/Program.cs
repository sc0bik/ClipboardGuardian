using System.Windows.Forms;
using ClipboardGuardian.Win.Services;

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
