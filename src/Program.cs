using System.Text.Json;

namespace Editrainer;

static class Program
{
    static string? tracePath;
    internal static void Trace(string text)
    {
        if (tracePath != null) File.AppendAllText(tracePath, DateTime.Now.ToString("O") + " " + text + Environment.NewLine);
    }
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 2 && args[0].StartsWith("--")) tracePath = args[2] + ".trace.txt";
        Trace("Main started");
        try { Run(args); }
        catch (Exception ex)
        {
            var log = args.Length > 2 && args[0].StartsWith("--") ? args[2] + ".error.txt" : Path.Combine(AppContext.BaseDirectory, "error.log");
            File.WriteAllText(log, ex.ToString());
            if (args.Length == 0 || !args[0].StartsWith("--")) MessageBox.Show(ex.Message, "Editrainer");
        }
    }

    // Utility runs ignore what the user chose, so they look the same on any machine. The language and theme come last.
    static AppSettings UtilitySettings(string[] args)
    {
        int first = args[0] == "--preview" ? 9 : 4;
        return new(args.ElementAtOrDefault(first) ?? "es", args.ElementAtOrDefault(first + 1) == "dark");
    }

    static void Run(string[] args)
    {
        Trace("Configuring Windows Forms");
        ApplicationConfiguration.Initialize();
        Trace("Configuration complete");
        bool utility = args.Length > 0 && args[0] is "--preview" or "--ui-test" or "--layout-test" or "--settings-test" or "--settings-preview";
        string? path = utility ? args[1] : args.FirstOrDefault(File.Exists);
        string root = utility && args.Length > 3 ? args[3] : AppContext.BaseDirectory;
        var settings = utility ? UtilitySettings(args) : AppSettings.Load(root);
        settings.Apply();
        if (utility && args[0] == "--settings-preview") { SettingsDialog.SaveImage(settings, args[2]); return; }
        using var form = new MainForm(path, root, settings);
        if (!utility && args.Contains("--items")) form.Shown += (_, _) => form.View.OpenResources();
        if (!utility && args.Contains("--equipment")) form.Shown += (_, _) => form.View.OpenEquipment();
        if (utility && args[0] == "--preview" && args.Length > 7) form.Size = new Size(int.Parse(args[6]), int.Parse(args[7]));
        Trace("Form constructed");
        if (utility) Automate(form, args);
        Application.Run(form);
    }

    static void Automate(MainForm form, string[] args)
    {
        form.RunUnattended(); form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-16000, -16000);
        form.Shown += (_, _) => {
            Trace("Shown");
            var timer = new System.Windows.Forms.Timer { Interval = 400 };
            timer.Tick += (_, _) => {
                timer.Stop(); timer.Dispose();
                Trace("Timer callback");
                try {
                    if (args[0] == "--ui-test") form.View.RunInteractionChecks(args[2]);
                    else if (args[0] == "--layout-test") form.View.RunLayoutChecks(args[2], form.Settings);
                    else if (args[0] == "--settings-test") form.RunSettingsChecks(args[2]);
                    else if (args.Length > 8 && args[8] is "trainer" or "pokedex") form.View.CaptureTool(args[2], args[8]);
                    else form.View.CaptureScreenshot(args[2], args.Length > 4 ? int.Parse(args[4]) : 0, args.Length > 5 ? int.Parse(args[5]) : 0, args.Length > 8 && args[8] == "shiny");
                } catch (Exception ex) {
                    File.WriteAllText(args[2] + ".error.txt", ex.ToString());
                    if (args[0] is "--ui-test" or "--layout-test" or "--settings-test") form.View.CaptureScreenshot(args[2] + ".failure.png", 1, 1);
                }
                form.Close();
            };
            timer.Start();
        };
    }
}
