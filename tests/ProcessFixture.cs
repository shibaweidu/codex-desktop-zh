using System;
using System.Windows.Forms;

internal static class ProcessFixture
{
    [STAThread]
    private static void Main(string[] args)
    {
        var mode = args.Length == 0 ? "graceful" : args[0].ToLowerInvariant();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = new Form
        {
            Text = "Codex process fixture",
            Width = 320,
            Height = 180,
            ShowInTaskbar = true
        };
        if (mode == "resist")
        {
            form.FormClosing += delegate(object sender, FormClosingEventArgs eventArgs)
            {
                eventArgs.Cancel = true;
            };
        }
        if (mode == "autoexit")
        {
            var timer = new Timer { Interval = 250 };
            timer.Tick += delegate
            {
                timer.Stop();
                form.Close();
            };
            timer.Start();
        }
        Application.Run(form);
    }
}
