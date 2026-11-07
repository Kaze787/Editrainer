using System.Globalization;
using System.Text.Json;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>What the user chose, kept next to the app.</summary>
public sealed record AppSettings(string Language = "es", bool Dark = false)
{
    const string FileName = "settings.json";

    public static AppSettings Load(string root)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(root, FileName))) ?? FirstRun(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return FirstRun(); }
    }

    // Until the user chooses, the app speaks the language of Windows, or English if it doesn't have that one.
    internal static AppSettings FirstRun()
    {
        var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return new(Languages.Any(l => l.Code == system) ? system : "en");
    }

    // A folder that can't be written only means the choice lasts until the app closes.
    public void Save(string root)
    {
        try { File.WriteAllText(Path.Combine(root, FileName), JsonSerializer.Serialize(this)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Program.Trace("Settings not saved: " + e.Message); }
    }

    public void Apply()
    {
        Catalog.UseLanguage(Language);
        Theme.Use(Dark);
    }
}

/// <summary>Theme and language. Applying them replaces what's inside the window, so nothing changes until confirmed.</summary>
sealed class SettingsDialog : Form
{
    readonly RadioButton light = new() { Text = T("Claro"), AutoSize = true, Font = Theme.Font(10), Margin = new Padding(0, 6, 18, 6) };
    readonly RadioButton dark = new() { Text = T("Oscuro"), AutoSize = true, Font = Theme.Font(10), Margin = new Padding(0, 6, 6, 6) };
    readonly ComboBox language = Theme.Combo();
    readonly Button apply = Theme.Button(T("Aplicar"), true);

    SettingsDialog(AppSettings current)
    {
        Text = T("Configuración"); Font = Theme.Font(); BackColor = Theme.Background; ForeColor = Theme.Ink;
        ClientSize = new Size(400, 200); FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(16, 14, 16, 10) };
        layout.ColumnStyles.Add(new(SizeType.AutoSize)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 48)); layout.RowStyles.Add(new(SizeType.Absolute, 48)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        var themes = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        themes.Controls.Add(light); themes.Controls.Add(dark);
        layout.Controls.Add(Title(T("Tema")), 0, 0); layout.Controls.Add(themes, 1, 0);
        language.Items.AddRange(Languages.Cast<object>().ToArray());
        language.SelectedItem = Languages.FirstOrDefault(l => l.Code == current.Language) ?? Languages[0];
        layout.Controls.Add(Title(T("Idioma")), 0, 1); layout.Controls.Add(language, 1, 1);
        (current.Dark ? dark : light).Checked = true;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var cancel = Theme.Button(T("Cancelar"));
        apply.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(apply); buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 2); layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout); AcceptButton = apply; CancelButton = cancel;
    }

    static Label Title(string text)
    {
        var label = Theme.Label(text, 10, true); label.AutoSize = true; label.Margin = new Padding(3, 10, 18, 10);
        return label;
    }

    AppSettings Chosen => new(((Strings.Language)language.SelectedItem!).Code, dark.Checked);

    public static AppSettings? Pick(IWin32Window owner, AppSettings current)
    {
        using var dialog = new SettingsDialog(current);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Chosen : null;
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void Confirm(AppSettings chosen)
    {
        language.SelectedItem = Languages.First(l => l.Code == chosen.Language);
        (chosen.Dark ? dark : light).Checked = true;
        apply.PerformClick();
    }

    /// <summary>For the screenshots, which can't wait on a dialog.</summary>
    internal static void SaveImage(AppSettings current, string path)
    {
        using var dialog = new SettingsDialog(current) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, -16000) };
        dialog.Show(); dialog.Refresh();
        using var bitmap = Theme.Capture(dialog);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        dialog.Close();
    }
}
