using System.Globalization;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>Seen and caught for the open save. What changes here stays pending until the game is saved.</summary>
sealed class PokedexDialog : Form
{
    const int NumberColumn = 0, NameColumn = 1, SeenColumn = 2, CaughtColumn = 3;
    readonly PokedexEditor editor;
    readonly TextBox search = new() { PlaceholderText = T("Buscar Pokémon…"), Dock = DockStyle.Fill, Font = Theme.Font(), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 5, 12, 5) };
    readonly Label counts = Theme.Label("", 10, true, Theme.Accent);
    readonly DataGridView grid = new() { Dock = DockStyle.Fill, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, AllowUserToResizeColumns = false, RowHeadersVisible = false, AutoGenerateColumns = false,
        BackgroundColor = Theme.Surface, BorderStyle = Theme.IsDark ? BorderStyle.None : BorderStyle.FixedSingle, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Theme.Line, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 30, RowTemplate = { Height = 28 }, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing };
    readonly Button seenAll = Theme.Button(T("Marcar todos vistos")), caughtAll = Theme.Button(T("Marcar todos capturados")), undo = Theme.Button(T("Deshacer Pokédex"));
    bool binding;

    public PokedexDialog(SaveDocument document)
    {
        editor = document.Pokedex;
        Text = T("Pokédex"); Font = Theme.Font(); BackColor = Theme.Background; ForeColor = Theme.Ink;
        Size = new Size(760, 680); MinimumSize = new Size(760, 480); StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16, 12, 16, 10) };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        Controls.Add(layout);

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        top.ColumnStyles.Add(new(SizeType.Percent, 100)); top.ColumnStyles.Add(new(SizeType.AutoSize));
        counts.AutoSize = true; counts.Dock = DockStyle.None; counts.Anchor = AnchorStyles.Right; top.Controls.Add(search, 0, 0); top.Controls.Add(counts, 1, 0);
        layout.Controls.Add(top, 0, 0);

        grid.DefaultCellStyle.Font = Theme.Font(10); grid.DefaultCellStyle.ForeColor = Theme.Ink; grid.DefaultCellStyle.BackColor = Theme.Surface;
        grid.DefaultCellStyle.SelectionBackColor = Theme.AccentLight; grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = Theme.Font(9, true); grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Background;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Background; grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Muted;
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("N.º"), Width = 66, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("Pokémon"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 150, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = T("Visto"), Width = 110, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = T("Capturado"), Width = 130, SortMode = DataGridViewColumnSortMode.NotSortable });
        layout.Controls.Add(grid, 0, 1);

        string note = T("Marcar registra cada especie y conserva las formas, shinies e idiomas que ya tengas.");
        if (!editor.CanEdit) note = T("Esta versión de la partida guarda la Pokédex en un formato anterior. Puedes consultarla, pero no cambiarla aquí.");
        else if (!editor.CanClear) note += "\n" + T("En este juego el progreso viene de la investigación, así que lo ya registrado no se puede desmarcar.");
        var hint = Theme.Label(note + "\n" + T("Los cambios se aplican al guardar la partida."), 9, color: Theme.Muted);
        hint.AutoSize = true; hint.Margin = new Padding(3, 8, 3, 6); hint.MaximumSize = new Size(ClientSize.Width - 46, 0);
        layout.Controls.Add(hint, 0, 2);

        var close = Theme.Button(T("Cerrar"), true); close.DialogResult = DialogResult.OK;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
        buttons.Controls.Add(close); buttons.Controls.Add(undo); buttons.Controls.Add(caughtAll); buttons.Controls.Add(seenAll);
        layout.Controls.Add(buttons, 0, 3);
        AcceptButton = CancelButton = close; ActiveControl = grid;
        seenAll.Visible = caughtAll.Visible = undo.Visible = editor.CanEdit;

        search.TextChanged += (_, _) => FillRows();
        seenAll.Click += (_, _) => { editor.MarkAll(caught: false); FillRows(); };
        caughtAll.Click += (_, _) => { editor.MarkAll(caught: true); FillRows(); };
        undo.Click += (_, _) => { editor.Reset(); FillRows(); };
        // A tick counts when it is made, not when the cell is left.
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += (_, e) => { if (!binding && e.RowIndex >= 0) Toggled(grid.Rows[e.RowIndex]); };
        FillRows();
    }

    ushort SpeciesOf(DataGridViewRow row) => (ushort)row.Tag!;

    void Toggled(DataGridViewRow row)
    {
        bool seen = (bool)row.Cells[SeenColumn].Value!, caught = (bool)row.Cells[CaughtColumn].Value!;
        var before = editor.Get(SpeciesOf(row));
        // Clearing "seen" takes "caught" with it, and marking "caught" brings "seen".
        editor.Set(SpeciesOf(row), seen, caught && (seen || !before.Seen));
        Show(row);
        ShowCounts();
    }

    void Show(DataGridViewRow row)
    {
        ushort species = SpeciesOf(row);
        var state = editor.Get(species);
        var original = editor.Original(species);
        binding = true;
        row.Cells[SeenColumn].Value = state.Seen; row.Cells[CaughtColumn].Value = state.Caught;
        row.Cells[NameColumn].Value = Catalog.Names.specieslist[species] + (state != original ? "  ●" : "");
        row.Cells[SeenColumn].ReadOnly = !editor.CanEdit || !editor.CanClear && original.Seen;
        row.Cells[CaughtColumn].ReadOnly = !editor.CanEdit || !editor.CanClear && original.Caught;
        row.DefaultCellStyle.ForeColor = state != original ? Theme.Accent : Theme.Ink;
        binding = false;
    }

    void FillRows()
    {
        string query = search.Text.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        var rows = editor.Entries.Where(species => query.Length == 0 || species.ToString() == query
            || compare.IndexOf(Catalog.Names.specieslist[species], query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).Select(species => {
                var row = new DataGridViewRow { Tag = species };
                row.CreateCells(grid, species.ToString("000"), "", false, false);
                return row;
            }).ToArray();
        binding = true;
        grid.Rows.Clear(); grid.Rows.AddRange(rows);
        binding = false;
        foreach (var row in rows) Show(row);
        ShowCounts();
    }

    void ShowCounts()
    {
        counts.Text = T("Vistos: {0} / {1}  ·  Capturados: {2} / {3}", editor.SeenCount, editor.Entries.Count, editor.CaughtCount, editor.Entries.Count);
        undo.Enabled = editor.ChangeCount != 0;
    }

    DataGridViewRow Row(ushort species) => grid.Rows.Cast<DataGridViewRow>().First(r => SpeciesOf(r) == species);

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void RunInteractionChecks(SaveDocument document, Action<bool, string> check)
    {
        check(grid.Rows.Count == editor.Entries.Count, "The Pokédex window lists every species of the game");
        check(!document.HasChanges && !undo.Enabled, "Opening the Pokédex window stages nothing");
        ushort unseen = editor.Entries.First(s => !editor.Get(s).Seen);
        var row = Row(unseen);
        row.Cells[CaughtColumn].Value = true;
        check(editor.Get(unseen) == new DexState(true, true) && (bool)row.Cells[SeenColumn].Value! && document.HasChanges, "Marking a species caught marks it seen and stages the change");
        check(row.Cells[NameColumn].Value!.ToString()!.Contains('●') && undo.Enabled, "A pending species stands out");
        check(!document.Save.GetSeen(unseen), "The open game doesn't change until it is saved");
        row.Cells[SeenColumn].Value = false;
        check(editor.Get(unseen) == new DexState(false, false) && !(bool)row.Cells[CaughtColumn].Value! && !document.HasChanges, "Clearing seen also clears caught, and going back leaves nothing pending");
        row.Cells[SeenColumn].Value = true;
        check(editor.Get(unseen) == new DexState(true, false), "A species can be only seen");
        search.Text = Catalog.Names.specieslist[unseen];
        check(grid.Rows.Count >= 1 && (bool)Row(unseen).Cells[SeenColumn].Value!, "Search keeps the pending marks");
        search.Text = unseen.ToString();
        check(grid.Rows.Count == 1 && SpeciesOf(grid.Rows[0]) == unseen, "Search also finds the number");
        search.Text = "pokemon-que-no-existe";
        check(grid.Rows.Count == 0, "A search with no match leaves the list empty");
        search.Text = "";
        seenAll.PerformClick();
        check(editor.SeenCount == editor.Entries.Count && counts.Text.Contains($"{editor.Entries.Count} / {editor.Entries.Count}"), "Marking all as seen fills the count");
        caughtAll.PerformClick();
        check(editor.CaughtCount == editor.Entries.Count, "Marking all as caught fills the count");
        check(EditorView.Clipped(this).Length == 0, "Pokédex window texts fit");
        undo.PerformClick();
        check(!document.HasChanges && !undo.Enabled && grid.Rows.Count == editor.Entries.Count, "Undo clears the whole Pokédex draft");
    }
}
