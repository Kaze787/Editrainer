using PKHeX.Core;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>
/// A window beside the editor for a tool that works on the whole save. It stays open so that both can
/// be used at once, and the editor closes it when the save it was looking at is replaced.
/// </summary>
abstract class ToolWindow : Form
{
    const int EditorMargin = 24, ExtraRow = 1, DetailsRow = 3;
    readonly TableLayoutPanel body = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(16, 10, 16, 8) };
    readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
    readonly Label status = Theme.Label("", 9, color: Theme.Muted);
    protected readonly DataGridView Grid = new() { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, AutoGenerateColumns = false,
        BackgroundColor = Theme.Surface, BorderStyle = Theme.IsDark ? BorderStyle.None : BorderStyle.FixedSingle, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Theme.Line, EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        ColumnHeadersHeight = 30, RowTemplate = { Height = 28 }, Margin = new Padding(3, 4, 3, 6) };
    protected readonly TextBox Details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = Theme.Font(9),
        BackColor = Theme.Surface, ForeColor = Theme.Ink, BorderStyle = BorderStyle.None, Margin = new Padding(3, 2, 3, 2), Visible = false };

    /// <summary>True while it is still finding out what to show.</summary>
    internal bool Working;
    internal string StatusText => status.Text;

    protected ToolWindow(string title, Size size, Size smallest)
    {
        Text = title; Font = Theme.Font(); BackColor = Theme.Background; ForeColor = Theme.Ink;
        Size = size; MinimumSize = smallest; MinimizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
        Grid.DefaultCellStyle.Font = Theme.Font(9); Grid.DefaultCellStyle.ForeColor = Theme.Ink; Grid.DefaultCellStyle.BackColor = Theme.Surface;
        Grid.DefaultCellStyle.SelectionBackColor = Theme.AccentLight; Grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        Grid.ColumnHeadersDefaultCellStyle.Font = Theme.Font(9, true); Grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Background;
        Grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted; Grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Background;
        Grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Muted;
        body.RowStyles.Add(new(SizeType.Absolute, 34)); body.RowStyles.Add(new(SizeType.AutoSize)); body.RowStyles.Add(new(SizeType.Percent, 100));
        body.RowStyles.Add(new(SizeType.Absolute, 0)); body.RowStyles.Add(new(SizeType.Absolute, 40)); body.RowStyles.Add(new(SizeType.Absolute, 48));
        body.Controls.Add(Theme.Label(title, 11, true), 0, 0);
        body.Controls.Add(Grid, 0, 2); body.Controls.Add(Details, 0, DetailsRow); body.Controls.Add(status, 0, 4); body.Controls.Add(actions, 0, 5);
        var close = Theme.Button(T("Cerrar")); close.Click += (_, _) => Close();
        actions.Controls.Add(close); CancelButton = close;
        Controls.Add(body);
    }

    /// <summary>
    /// Over the corner of the editor that hides the least of it, so what a click selects there can be seen, and
    /// shrunk and moved as needed to keep every button on screen. An editor that is off the screen altogether, as
    /// in the automatic checks, takes the tool with it. If even the smallest size doesn't fit, the title bar stays in reach.
    /// </summary>
    internal static Rectangle Place(Rectangle editor, Size size, Size smallest, Rectangle screen)
    {
        if (!screen.IntersectsWith(editor)) return new(editor.Right - size.Width - EditorMargin, editor.Bottom - size.Height - EditorMargin, size.Width, size.Height);
        int width = Math.Max(smallest.Width, Math.Min(size.Width, screen.Width));
        int height = Math.Max(smallest.Height, Math.Min(size.Height, screen.Height));
        int left = Math.Max(screen.Left, Math.Min(editor.Right - width - EditorMargin, screen.Right - width));
        int top = Math.Max(screen.Top, Math.Min(editor.Bottom - height - EditorMargin, screen.Bottom - height));
        return new(left, top, width, height);
    }

    internal void MoveBeside(Form editor)
    {
        StartPosition = FormStartPosition.Manual;
        Bounds = Place(editor.Bounds, Size, MinimumSize, Screen.FromControl(editor).WorkingArea);
    }

    public void ShowBeside(Form editor)
    {
        MoveBeside(editor);
        Show(editor);
    }

    protected void AddAbove(Control control) => body.Controls.Add(control, 0, ExtraRow);
    protected void AddAction(Button button) => actions.Controls.Add(button);
    protected void ShowDetails(int height) { body.RowStyles[DetailsRow].Height = height; Details.Visible = true; }
    protected void Say(string text, bool problem = false) { status.Text = text; status.ForeColor = problem ? Theme.Danger : Theme.Muted; }

    protected void SetColumns(IEnumerable<string> headers, bool stretchLast)
    {
        foreach (var header in headers) Grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, MinimumWidth = 40 });
        if (stretchLast) Grid.Columns[^1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    // Built apart and added at once, and only then measured: a large save has a thousand rows.
    protected void Fill(IEnumerable<(object Tag, object[] Cells)> rows)
    {
        var sorted = Grid.SortedColumn; var order = Grid.SortOrder;
        Grid.Rows.Clear();
        Grid.Rows.AddRange(rows.Select(r => { var row = new DataGridViewRow { Tag = r.Tag }; row.CreateCells(Grid, r.Cells); return row; }).ToArray());
        Grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
        if (sorted != null) Grid.Sort(sorted, order == SortOrder.Descending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending);
    }

    protected object? SelectedTag => Grid.CurrentRow?.Tag;

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal int RowCount => Grid.Rows.Count;
    internal string Cell(int row, int column) => Grid.Rows[row].Cells[column].FormattedValue?.ToString() ?? "";
    internal string DetailText => Details.Text;
    internal void SelectRow(int row) => Grid.CurrentCell = Grid.Rows[row].Cells[0];
    internal void SortBy(int column) => Grid.Sort(Grid.Columns[column], System.ComponentModel.ListSortDirection.Ascending);
    internal bool ButtonsFit => actions.Controls.Cast<Control>().All(c => c.Left >= 0);
}

/// <summary>A tool that goes through every Pokémon, in the background so the window keeps answering.</summary>
abstract class ScanWindow : ToolWindow
{
    readonly Action<PokemonEntry> showInEditor;
    readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 12, Margin = new Padding(3, 2, 3, 6), Visible = false };
    readonly Button again = Theme.Button(T("Comprobar de nuevo"));
    CancellationTokenSource? running;

    protected readonly SaveDocument Document;
    internal List<PokemonRow> Rows { get; private set; } = [];

    protected ScanWindow(string title, Size size, Size smallest, SaveDocument document, Action<PokemonEntry> showInEditor) : base(title, size, smallest)
    {
        Document = document; this.showInEditor = showInEditor; Working = true;
        AddAbove(progress); AddAction(again);
        again.Click += async (_, _) => await Run();
        Grid.CellClick += (_, e) => { if (e.RowIndex >= 0) ShowInEditor(e.RowIndex); };
        Shown += async (_, _) => await Run();
        FormClosed += (_, _) => running?.Cancel();
    }

    protected abstract void ShowRows();

    async Task Run()
    {
        if (running != null) return;
        var frozen = SaveTools.Freeze(Document);
        int total = frozen.Count;
        using var cancel = running = new CancellationTokenSource();
        Working = true; again.Enabled = false; progress.Maximum = Math.Max(1, total); progress.Value = 0; progress.Visible = true;
        Say(T("Comprobando {0} de {1}…", 0, total));
        var done = new Progress<int>(count => {
            if (running == null) return;
            progress.Value = count; Say(T("Comprobando {0} de {1}…", count, total));
        });
        try
        {
            var rows = await Task.Run(() => SaveTools.Scan(frozen, done, cancel.Token), cancel.Token);
            if (IsDisposed) return;
            Rows = rows; ShowRows();
        }
        catch (OperationCanceledException) { }
        // Nothing here may reach the message loop, which would close the app with unsaved edits in it.
        catch (Exception e) { if (!IsDisposed) Say(e.Message, true); }
        finally
        {
            running = null; Working = false;
            if (!IsDisposed) { progress.Visible = false; again.Enabled = true; }
        }
    }

    void ShowInEditor(int row)
    {
        if (Grid.Rows[row].Tag is PokemonRow chosen) showInEditor(chosen.Entry);
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void PressRow(int row) { SelectRow(row); ShowInEditor(row); }
    internal void PressAgain() => again.PerformClick();
    internal int RowOf(PokemonEntry entry) => Grid.Rows.Cast<DataGridViewRow>().ToList().FindIndex(r => ((PokemonRow)r.Tag!).Entry == entry);
}

/// <summary>Which Pokémon aren't legal as they would be saved, and why.</summary>
sealed class AuditWindow : ScanWindow
{
    public AuditWindow(SaveDocument document, Action<PokemonEntry> showInEditor)
        : base(T("Auditoría de legalidad"), new Size(780, 540), new Size(640, 440), document, showInEditor)
    {
        SetColumns([T("Lugar"), T("Pokémon"), T("Motivos")], true);
        ShowDetails(110);
        Grid.CurrentCellChanged += (_, _) => ShowSelected();
    }

    void ShowSelected() => Details.Text = (SelectedTag as PokemonRow)?.Report.ReplaceLineEndings("\r\n") ?? "";

    protected override void ShowRows()
    {
        var illegal = Rows.Where(r => !r.Legal).ToList();
        Fill(illegal.Select(r => ((object)r, new object[] { r.Place, r.Name, r.Reasons })));
        ShowSelected();
        Say(illegal.Count == 0 ? T("Los {0} Pokémon son legales.", Rows.Count)
            : $"{T("{0} de {1} Pokémon no son legales.", illegal.Count, Rows.Count)} {T("Haz clic en uno para verlo en el editor.")}");
    }
}

/// <summary>Every Pokémon in one table, which can leave for a spreadsheet.</summary>
sealed class BoxReportWindow : ScanWindow
{
    readonly List<ReportColumn> columns;
    readonly Button export = Theme.Button(T("Exportar CSV…"), true);

    public BoxReportWindow(SaveDocument document, Action<PokemonEntry> showInEditor)
        : base(T("Informe de cajas"), new Size(1060, 580), new Size(760, 420), document, showInEditor)
    {
        var rules = document.Rules;
        columns = SaveTools.ReportColumns(rules);
        SetColumns(columns.Select(c => c.Header), false);
        foreach (DataGridViewColumn column in Grid.Columns)
            if (column.HeaderText == rules.IVName || column.HeaderText == rules.EVName) column.ToolTipText = T("En este orden: {0}", string.Join(" / ", Catalog.Stats));
        export.Enabled = false; export.Click += (_, _) => PickFile();
        AddAction(export);
    }

    protected override void ShowRows()
    {
        Fill(Rows.Select(r => ((object)r, columns.Select(c => c.Value(r)).ToArray())));
        export.Enabled = true;
        Say(T("{0} Pokémon en el informe.", Rows.Count));
    }

    void PickFile()
    {
        using var dialog = new SaveFileDialog { Title = T("Guardar el informe como CSV"), Filter = T("Hoja de cálculo CSV") + "|*.csv", FileName = T("Informe de cajas") + ".csv", OverwritePrompt = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) ExportTo(dialog.FileName);
    }

    internal void ExportTo(string path)
    {
        try { SaveTools.ExportCsv(path, Rows, columns); Say(T("Informe guardado: {0} Pokémon.", Rows.Count)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Say(e.Message, true); }
    }
}

/// <summary>The folders in Backups, and the way back to any of them.</summary>
sealed class BackupWindow : ToolWindow
{
    readonly string root;
    readonly SaveDocument? document;
    readonly Action<string> restored;
    readonly Button restore = Theme.Button(T("Restaurar…"), true);

    /// <summary>Stand-ins for the checks, which can't answer a real message box or depend on what is running.</summary>
    internal Func<string, bool> Confirm;
    internal Func<SaveFile, string?> FindEmulator = SaveDocument.RunningEmulator;

    public BackupWindow(string root, SaveDocument? document, Action<string> restored)
        : base(T("Administrar respaldos"), new Size(820, 540), new Size(640, 440))
    {
        this.root = root; this.document = document; this.restored = restored; Working = true;
        Confirm = text => MessageBox.Show(this, text, T("Restaurar respaldo"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        SetColumns([T("Fecha"), T("Juego"), T("Cambios")], true);
        Grid.Columns[0].DefaultCellStyle.Format = "g";
        ShowDetails(110);
        restore.Enabled = false; restore.Click += (_, _) => Restore();
        AddAction(restore);
        Grid.CurrentCellChanged += (_, _) => ShowSelected();
        Shown += (_, _) => { Reload(); Working = false; };
    }

    BackupInfo? Selected => SelectedTag as BackupInfo;

    void Reload()
    {
        try
        {
            var backups = SaveTools.ListBackups(root);
            Fill(backups.Select(b => ((object)b, new object[] { b.Date, b.Game, b.Summary })));
            Say(backups.Count switch { 0 => T("Aún no hay respaldos."), 1 => T("1 respaldo."), _ => T("{0} respaldos.", backups.Count) });
            ShowSelected();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Say(e.Message, true); }
    }

    void ShowSelected()
    {
        var backup = Selected;
        restore.Enabled = backup?.CanRestore == true;
        Details.Text = backup == null ? "" : string.Join("\r\n", new[] { backup.Target ?? "" }.Concat(backup.Changes).Where(line => line.Length != 0));
    }

    bool IsOpenFile(string path) => document != null && string.Equals(Path.GetFullPath(path), document.FilePath, StringComparison.OrdinalIgnoreCase);

    void Restore()
    {
        if (Selected is not { CanRestore: true } backup) return;
        string question = T("Se reemplazará este archivo por el respaldo del {0}:\n{1}\n\nAntes se guardará una copia del archivo actual. ¿Quieres continuar?", backup.Date.ToString("g"), backup.Target);
        if (IsOpenFile(backup.Target!) && document!.HasChanges) question += "\n\n" + T("Los cambios sin guardar de esta partida se perderán.");
        if (!Confirm(question)) return;
        try
        {
            SaveTools.RestoreBackup(backup, root, FindEmulator);
            Reload(); Say(T("Respaldo restaurado y verificado byte a byte."));
            restored(backup.Target!);
        }
        catch (Exception e) { Say(e.Message, true); }
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void PressRestore() => restore.PerformClick();
    internal bool CanPressRestore => restore.Enabled;
}

/// <summary>What differs between two saves of the same game.</summary>
sealed class CompareWindow : ToolWindow
{
    readonly TextBox first = PathBox(), second = PathBox();
    readonly Button compare = Theme.Button(T("Comparar"), true);

    public CompareWindow(string? openSave) : base(T("Comparar partidas"), new Size(880, 560), new Size(700, 460))
    {
        first.Text = openSave ?? "";
        var files = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
        files.ColumnStyles.Add(new(SizeType.AutoSize)); files.ColumnStyles.Add(new(SizeType.Percent, 100)); files.ColumnStyles.Add(new(SizeType.AutoSize));
        AddFile(files, 0, T("Partida A"), first); AddFile(files, 1, T("Partida B"), second);
        AddAbove(files);
        SetColumns([T("Tipo"), T("Lugar u objeto"), T("Partida A"), T("Partida B"), T("Diferencias")], true);
        Say(T("Elige dos partidas del mismo juego."));
        compare.Click += async (_, _) => await Run();
        AddAction(compare); AcceptButton = compare;
    }

    static TextBox PathBox() => new() { Dock = DockStyle.Fill, Font = Theme.Font(9), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 6, 4, 4) };

    void AddFile(TableLayoutPanel files, int row, string label, TextBox path)
    {
        var title = Theme.Label(label, 10, true); title.AutoSize = true; title.Dock = DockStyle.None; title.Anchor = AnchorStyles.Left; title.Margin = new Padding(3, 0, 12, 0);
        var browse = Theme.Button(T("Examinar…")); browse.Click += (_, _) => Browse(path);
        files.Controls.Add(title, 0, row); files.Controls.Add(path, 1, row); files.Controls.Add(browse, 2, row);
    }

    void Browse(TextBox path)
    {
        using var dialog = new OpenFileDialog { Title = T("Elige una partida"), Filter = T("Partidas de Pokémon") + "|main;savedata.bin;*.sav;*.srm;*.dsv;*.sa1;*.sa2;*.sa3;*.gci;*.bin;*.dat;*.raw;*.original|" + T("Todos los archivos") + "|*.*" };
        if (File.Exists(path.Text)) dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(path.Text));
        if (dialog.ShowDialog(this) == DialogResult.OK) path.Text = dialog.FileName;
    }

    async Task Run()
    {
        if (Working) return;
        string a = first.Text.Trim(), b = second.Text.Trim();
        if (a.Length == 0 || b.Length == 0) { Say(T("Elige dos partidas del mismo juego."), true); return; }
        Working = true; compare.Enabled = false; Say(T("Comparando…"));
        try
        {
            var changes = await Task.Run(() => SaveTools.Compare(SaveDocument.Read(a), SaveDocument.Read(b)));
            if (IsDisposed) return;
            Fill(changes.Select(c => ((object)c, new object[] { c.Kind, c.What, c.First, c.Second, c.Detail })));
            Say(changes.Count switch { 0 => T("No hay diferencias en Pokémon, dinero ni objetos."), 1 => T("1 diferencia."), _ => T("{0} diferencias.", changes.Count) });
        }
        // Any file can be chosen here, and whatever it does must not reach the message loop.
        catch (Exception e)
        {
            if (IsDisposed) return;
            Grid.Rows.Clear(); Say(e.Message, true);
        }
        finally
        {
            Working = false;
            if (!IsDisposed) compare.Enabled = true;
        }
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal string FirstPath => first.Text;
    internal void PressCompare(string a, string b) { first.Text = a; second.Text = b; compare.PerformClick(); }
}
