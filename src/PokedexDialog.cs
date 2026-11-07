using System.Globalization;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>Seen and caught for the open save, through each Pokédex of its game. What changes here stays pending until the game is saved.</summary>
sealed class PokedexDialog : Form
{
    const int NumberColumn = 0, NationalColumn = 1, NameColumn = 2, SeenColumn = 3, CaughtColumn = 4;
    const int ListWidth = 270, HeaderMargin = 10, ArrowWidth = 24, FewestRows = 4, HintMargin = 46;
    readonly PokedexEditor editor;
    readonly ComboBox lists = Theme.Combo();
    readonly TextBox search = new() { PlaceholderText = T("Buscar Pokémon…"), Dock = DockStyle.Fill, Font = Theme.Font(), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 6, 3, 4) };
    readonly Label counts = Theme.Label("", 10, true, Theme.Accent);
    readonly DataGridView grid = new() { Dock = DockStyle.Fill, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, AllowUserToResizeColumns = false, RowHeadersVisible = false, AutoGenerateColumns = false,
        BackgroundColor = Theme.Surface, BorderStyle = Theme.IsDark ? BorderStyle.None : BorderStyle.FixedSingle, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Theme.Line, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 30, RowTemplate = { Height = 28 }, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing };
    readonly Button seenAll = Theme.Button(T("Marcar todos vistos")), caughtAll = Theme.Button(T("Marcar todos capturados")), undo = Theme.Button(T("Deshacer Pokédex"));
    readonly Label hint;
    readonly FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
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

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        top.ColumnStyles.Add(new(SizeType.Absolute, ListWidth)); top.ColumnStyles.Add(new(SizeType.Percent, 100));
        top.RowStyles.Add(new(SizeType.AutoSize)); top.RowStyles.Add(new(SizeType.AutoSize));
        lists.Margin = new Padding(3, 6, 8, 4); lists.Enabled = editor.Lists.Count > 1;
        lists.Items.AddRange(editor.Lists.Select(list => (object)T(list.Name)).ToArray());
        lists.SelectedIndex = 0;
        counts.AutoSize = true; counts.Dock = DockStyle.None; counts.Anchor = AnchorStyles.Right; counts.Margin = new Padding(3, 2, 3, 4);
        top.Controls.Add(lists, 0, 0); top.Controls.Add(search, 1, 0); top.Controls.Add(counts, 0, 1); top.SetColumnSpan(counts, 2);
        layout.Controls.Add(top, 0, 0);

        grid.DefaultCellStyle.Font = Theme.Font(10); grid.DefaultCellStyle.ForeColor = Theme.Ink; grid.DefaultCellStyle.BackColor = Theme.Surface;
        grid.DefaultCellStyle.SelectionBackColor = Theme.AccentLight; grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = Theme.Font(9, true); grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Background;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Background; grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Muted;
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("N.º"), Width = 66, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("Nacional"), Width = 90, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("Pokémon"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 150, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = T("Visto"), Width = 110, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = T("Capturado"), Width = 130, SortMode = DataGridViewColumnSortMode.NotSortable });
        layout.Controls.Add(grid, 0, 1);

        string note = T("Marcar registra cada especie y conserva las formas, shinies e idiomas que ya tengas.");
        if (!editor.CanEdit) note = T("Esta versión de la partida guarda la Pokédex en un formato anterior. Puedes consultarla, pero no cambiarla aquí.");
        else if (!editor.CanClear) note += "\n" + T("En este juego el progreso viene de la investigación, así que lo ya registrado no se puede desmarcar.");
        if (editor.Lists.Count > 1)
        {
            note += "\n" + T("Una especie vista o capturada lo es en todas las Pokédex donde aparece.");
            if (editor.CanEdit) note += " " + T("«Marcar todos» solo afecta a la Pokédex elegida.");
        }
        if (editor.RegionalIsNational) note += "\n" + T("En este juego la Pokédex regional y la Nacional son la misma lista, con los mismos números.");
        if (editor.HasAllSpeciesList) note += "\n" + T("Este juego no tiene Pokédex Nacional. «Todas las especies» reúne las que la partida puede registrar, en orden nacional.");
        hint = Theme.Label(note + "\n" + T("Los cambios se aplican al guardar la partida."), 9, color: Theme.Muted);
        hint.AutoSize = true; hint.Margin = new Padding(3, 8, 3, 6); hint.MaximumSize = new Size(ClientSize.Width - HintMargin, 0);
        layout.Controls.Add(hint, 0, 2);

        var close = Theme.Button(T("Cerrar"), true); close.DialogResult = DialogResult.OK;
        buttons.Controls.Add(close); buttons.Controls.Add(undo); buttons.Controls.Add(caughtAll); buttons.Controls.Add(seenAll);
        layout.Controls.Add(buttons, 0, 3);
        AcceptButton = CancelButton = close; ActiveControl = grid;
        seenAll.Visible = caughtAll.Visible = undo.Visible = editor.CanEdit;

        search.TextChanged += (_, _) => FillRows();
        lists.SelectedIndexChanged += (_, _) => FillRows();
        seenAll.Click += (_, _) => { editor.MarkAll(Selected, caught: false); FillRows(); };
        caughtAll.Click += (_, _) => { editor.MarkAll(Selected, caught: true); FillRows(); };
        undo.Click += (_, _) => { editor.Reset(); FillRows(); };
        // A tick counts when it is made, not when the cell is left.
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += (_, e) => { if (!binding && e.RowIndex >= 0) Toggled(grid.Rows[e.RowIndex]); };
        FillRows();
    }

    // The longest languages need more room than the buttons have, and this row grows to the left, out of sight.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        int needed = buttons.GetPreferredSize(Size.Empty).Width + buttons.Parent!.Padding.Horizontal + Width - ClientSize.Width;
        if (needed <= Width) return;
        MinimumSize = new Size(needed, MinimumSize.Height);
        Width = needed;
        hint.MaximumSize = new Size(ClientSize.Width - HintMargin, 0);
    }

    DexList Selected => editor.Lists[lists.SelectedIndex];

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
        var list = Selected;
        string query = search.Text.Trim();
        bool isNumber = int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out int number);
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        var rows = list.Entries.Where(entry => query.Length == 0 || isNumber && (entry.Number == number || entry.Species == number)
            || compare.IndexOf(Catalog.Names.specieslist[entry.Species], query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).Select(entry => {
                var row = new DataGridViewRow { Tag = entry.Species };
                row.CreateCells(grid, entry.Number.ToString("000"), entry.Species.ToString("000"), "", false, false);
                return row;
            }).ToArray();
        grid.Columns[NationalColumn].Visible = !list.HasNationalNumbers;
        binding = true;
        grid.Rows.Clear(); grid.Rows.AddRange(rows);
        binding = false;
        foreach (var row in rows) Show(row);
        ShowCounts();
    }

    void ShowCounts()
    {
        var list = Selected;
        counts.Text = T("Vistos: {0} / {1}  ·  Capturados: {2} / {3}", editor.SeenIn(list), list.Entries.Count, editor.CaughtIn(list), list.Entries.Count);
        undo.Enabled = editor.ChangeCount != 0;
    }

    DataGridViewRow Row(ushort species) => grid.Rows.Cast<DataGridViewRow>().First(r => SpeciesOf(r) == species);

    bool IsListed(ushort species) => grid.Rows.Cast<DataGridViewRow>().Any(r => SpeciesOf(r) == species);

    void Choose(DexList list) => lists.SelectedIndex = editor.Lists.ToList().IndexOf(list);

    // The rows, their numbers and the counts all answer to the Pokédex chosen.
    bool ShowsChosen()
    {
        var list = Selected;
        var rows = grid.Rows.Cast<DataGridViewRow>().ToList();
        return rows.Count == list.Entries.Count
            && rows.Zip(list.Entries).All(pair => SpeciesOf(pair.First) == pair.Second.Species
                && (string)pair.First.Cells[NumberColumn].Value! == pair.Second.Number.ToString("000")
                && (string)pair.First.Cells[NationalColumn].Value! == pair.Second.Species.ToString("000"))
            && grid.Columns[NationalColumn].Visible == !list.HasNationalNumbers
            && counts.Text == T("Vistos: {0} / {1}  ·  Capturados: {2} / {3}", editor.SeenIn(list), list.Entries.Count, editor.CaughtIn(list), list.Entries.Count);
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void RunInteractionChecks(SaveDocument document, Action<bool, string> check)
    {
        check(lists.Items.Cast<string>().SequenceEqual(editor.Lists.Select(list => T(list.Name))) && lists.SelectedIndex == 0, "The selector offers each Pokédex of the game by its name");
        check(lists.Enabled == editor.Lists.Count > 1, "The selector only opens when there is something to choose");
        foreach (var list in editor.Lists)
        {
            Choose(list);
            check(ShowsChosen(), $"Choosing {list.Name} shows its species in its order, with both numbers and its counts");
        }
        check(!document.HasChanges && !undo.Enabled, "Opening the Pokédex window stages nothing");
        var chosen = editor.Lists.First(list => list.Entries.Any(entry => !editor.Get(entry.Species).Seen));
        Choose(chosen);
        var unseen = chosen.Entries.First(entry => !editor.Get(entry.Species).Seen);
        ushort species = unseen.Species;
        var row = Row(species);
        row.Cells[CaughtColumn].Value = true;
        check(editor.Get(species) == new DexState(true, true) && (bool)row.Cells[SeenColumn].Value! && document.HasChanges, "Marking a species caught marks it seen and stages the change");
        check(row.Cells[NameColumn].Value!.ToString()!.Contains('●') && undo.Enabled, "A pending species stands out");
        check(!document.Save.GetSeen(species), "The open game doesn't change until it is saved");
        check(ShowsChosen(), "The counts of the chosen Pokédex follow the marks");
        var other = editor.Lists.FirstOrDefault(list => list != chosen && list.Entries.Any(entry => entry.Species == species));
        if (other != null)
        {
            Choose(other);
            check((bool)Row(species).Cells[CaughtColumn].Value! && Row(species).Cells[NameColumn].Value!.ToString()!.Contains('●') && ShowsChosen(), "A mark shows in every Pokédex with that species");
            Choose(chosen);
            row = Row(species);
        }
        row.Cells[SeenColumn].Value = false;
        check(editor.Get(species) == new DexState(false, false) && !(bool)row.Cells[CaughtColumn].Value! && !document.HasChanges, "Clearing seen also clears caught, and going back leaves nothing pending");
        row.Cells[SeenColumn].Value = true;
        check(editor.Get(species) == new DexState(true, false), "A species can be only seen");
        search.Text = Catalog.Names.specieslist[species];
        check(grid.Rows.Count >= 1 && (bool)Row(species).Cells[SeenColumn].Value!, "Search keeps the pending marks");
        search.Text = unseen.Number.ToString();
        check(IsListed(species), "Search finds the number in the chosen Pokédex");
        search.Text = unseen.Number.ToString("000");
        check(IsListed(species), "Search reads the number as the list writes it");
        search.Text = species.ToString();
        check(IsListed(species), "Search finds the national number");
        check(grid.Rows.Cast<DataGridViewRow>().All(r => SpeciesOf(r) == species || (string)r.Cells[NumberColumn].Value! == species.ToString("000") || Catalog.Names.specieslist[SpeciesOf(r)].Contains(species.ToString())), "A number finds nothing else but what carries it");
        search.Text = "pokemon-que-no-existe";
        check(grid.Rows.Count == 0, "A search with no match leaves the list empty");
        search.Text = "";
        int total = chosen.Entries.Count;
        var outside = editor.Entries.Except(chosen.Entries.Select(entry => entry.Species)).ToList();
        seenAll.PerformClick();
        check(editor.SeenIn(chosen) == total && counts.Text.Contains($"{total} / {total}"), "Marking all as seen fills the count of the chosen Pokédex");
        caughtAll.PerformClick();
        check(editor.CaughtIn(chosen) == total, "Marking all as caught fills the count of the chosen Pokédex");
        check(outside.All(s => editor.Get(s) == editor.Original(s)), "Marking all leaves the species of other Pokédexes alone");
        check(EditorView.Clipped(this).Length == 0, "Pokédex window texts fit");
        undo.PerformClick();
        check(!document.HasChanges && !undo.Enabled && ShowsChosen(), "Undo clears the whole Pokédex draft");
    }

    /// <summary>The selector and the columns, with the longest of every name a game can bring.</summary>
    internal void RunLayoutChecks(Action<bool, string> check, string sizeLabel)
    {
        var longest = PokedexEditor.ListNames.Select(T).MaxBy(name => TextRenderer.MeasureText(name, lists.Font).Width)!;
        check(TextRenderer.MeasureText(longest, lists.Font).Width + ArrowWidth <= lists.Width, $"Every Pokédex name fits the selector ({sizeLabel}) {longest}");
        foreach (var (label, size) in new[] { ("normal", Size), ("minimum", MinimumSize) })
        {
            Size = size; PerformLayout(); Application.DoEvents();
            foreach (var list in editor.Lists)
            {
                Choose(list); Application.DoEvents();
                var clipped = EditorView.Clipped(this);
                check(clipped.Length == 0, $"Every text fits with {list.Name} chosen ({sizeLabel}, {label} dialog) {clipped}");
                var hidden = buttons.Controls.Cast<Control>().FirstOrDefault(button => button.Left < 0);
                check(hidden == null, $"Every button fits ({sizeLabel}, {label} dialog) {hidden?.Text}");
                var cut = grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Index != NameColumn && TextRenderer.MeasureText(c.HeaderText, grid.ColumnHeadersDefaultCellStyle.Font).Width + HeaderMargin > c.Width);
                check(cut == null, $"Every column header fits ({sizeLabel}, {label} dialog) {cut?.HeaderText}");
                check(grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) <= grid.ClientSize.Width, $"The columns need no sideways scrolling with {list.Name} chosen ({sizeLabel}, {label} dialog)");
                check(grid.DisplayedRowCount(false) >= Math.Min(FewestRows, grid.Rows.Count), $"The list keeps room for its rows with {list.Name} chosen ({sizeLabel}, {label} dialog)");
            }
        }
    }
}
