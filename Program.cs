using System;
using System.Linq;
using System.Windows.Forms;

namespace MeetApp;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var checkUpdates = args.Contains("--check-updates", StringComparer.OrdinalIgnoreCase);
        Application.Run(new MainForm(args.FirstOrDefault(a => !a.StartsWith("--")), checkUpdates));
    }
}
