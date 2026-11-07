using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Editrainer;

/// <summary>The window. New settings replace what's inside it, so it never closes for them.</summary>
public sealed class MainForm : Form
{
    readonly string appRoot;
    readonly SpriteStore sprites;
    EditorView view;
    bool unattended;

    public AppSettings Settings { get; private set; }
    internal EditorView View => view;
    /// <summary>Automatic runs have nobody to ask about unsaved changes.</summary>
    internal void RunUnattended() => unattended = true;

    public MainForm(string? initialPath, string? root, AppSettings settings)
    {
        Settings = settings;
        appRoot = root ?? AppContext.BaseDirectory;
        sprites = new SpriteStore(appRoot);
        BackColor = Theme.Background; AutoScaleMode = AutoScaleMode.Dpi;
        var availableArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        MinimumSize = new Size(1200, 720);
        Size = new Size(Math.Min(1280, availableArea.Width - 32), Math.Min(900, availableArea.Height - 32));
        StartPosition = FormStartPosition.CenterScreen;
        var iconPath = Path.Combine(appRoot, "Assets", "app.ico");
        if (File.Exists(iconPath)) Icon = new Icon(iconPath);
        view = NewView();
        Shown += (_, _) => {
            if (initialPath != null) view.LoadFile(initialPath);
            else if (SaveLocator.FindNewest(appRoot) is { } detected) view.LoadFile(detected);
        };
        FormClosing += (_, e) => { if (!unattended && !view.CanDiscard()) e.Cancel = true; };
    }

    EditorView NewView()
    {
        var fresh = new EditorView(appRoot, sprites);
        fresh.TextChanged += (_, _) => Text = fresh.Text;
        fresh.SettingsRequested += OpenSettings;
        Controls.Add(fresh); Text = fresh.Text;
        return fresh;
    }

    // The pressed button belongs to the view being replaced, so the change waits until its click is over.
    void OpenSettings()
    {
        if (SettingsDialog.Pick(this, Settings) is { } chosen && chosen != Settings) BeginInvoke(() => ApplySettings(chosen));
    }

    void ApplySettings(AppSettings chosen)
    {
        var state = view.TakeSnapshot();
        chosen.Save(appRoot); Settings = chosen;
        Theme.WithoutRedraw(this, () => {
            chosen.Apply();
            state.Document?.RefreshNames();
            var replaced = view;
            view = NewView(); view.Resume(state);
            replaced.Dispose();
            BackColor = Theme.Background; Theme.DressTitleBar(this);
        });
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => view.HandleShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);

    protected override void Dispose(bool disposing)
    {
        if (disposing) sprites.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Changes the settings through the button and the dialog, there and back, keeping an eye on the window.</summary>
    internal void RunSettingsChecks(string reportPath)
    {
        var checks = new List<string>();
        void Check(bool ok, string description) { if (!ok) throw new Exception(description); checks.Add(description); }
        var system = CultureInfo.CurrentUICulture;
        foreach (var (culture, language) in new[] { ("es-MX", "es"), ("en-GB", "en"), ("zh-CN", "zh"), ("zh-TW", "zh"), ("fr-CA", "fr"), ("de-DE", "en"), ("pt-BR", "en") })
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Check(AppSettings.Load(Path.Combine(appRoot, "nothing-saved-here")).Language == language, $"The first run on a {culture} Windows uses {language}");
        }
        CultureInfo.CurrentUICulture = system;

        view.StagePendingEdits();
        var before = view.TakeSnapshot();
        var (window, bounds, first) = (Handle, Bounds, view);

        var clock = Stopwatch.StartNew();
        ChooseInDialog(new AppSettings("fr", true));
        clock.Stop();
        Check(Handle == window && Visible && Bounds == bounds, "The window stays open, in the same place");
        Check(first.IsDisposed && Controls.Count == 1 && Controls[0] == view, "Only what's inside the window is replaced");
        Check(Strings.Current.Code == "fr" && Theme.IsDark && BackColor == Theme.Background, "The new language and theme are in use");
        Check(AppSettings.Load(appRoot) == Settings, "The choice is remembered");
        Check(view.ContainsFocus, "The keyboard keeps working in the new view");
        Check(Text.Contains("modification"), $"The window title follows the language ({Text})");
        Check(TitleBarSettles(brightness => brightness < .3f), "The title bar turns dark");
        view.CheckResumed(before, "Enregistrer", "Bonbon Exp. XL", Check);

        ChooseInDialog(new AppSettings("es", false));
        Check(Handle == window && Controls.Count == 1, "The window also stays open on the way back");
        Check(Strings.Current.Code == "es" && !Theme.IsDark, "The original language and theme come back");
        Check(TitleBarSettles(brightness => brightness > .7f), "The title bar turns light");
        view.CheckResumed(before, "Guardar partida", "Caramelo Exp. XL", Check);

        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed = checks.Count, milliseconds = clock.ElapsedMilliseconds, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }

    void ChooseInDialog(AppSettings chosen)
    {
        var pressed = view;
        var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) => { timer.Dispose(); Application.OpenForms.OfType<SettingsDialog>().Single().Confirm(chosen); };
        timer.Start();
        pressed.PressSettings();
        while (view == pressed) Application.DoEvents();
    }

    // Windows fades the title bar from one theme to the other.
    bool TitleBarSettles(Func<float, bool> expected)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            using var picture = Theme.Capture(this);
            if (expected(picture.GetPixel(Width / 2, 12).GetBrightness())) return true;
            Thread.Sleep(20);
        }
        return false;
    }
}
