using PKHeX.Core;
using System.Globalization;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>What the small tools of each game share: a list, a note about it, a line for what just happened, and a row of buttons.</summary>
abstract class ExtraDialog : Form
{
    const int SmallestHeight = 460, StatusHeight = 40, NoteMargin = 46;
    protected const string PendingMark = "  ●";
    readonly TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16, 12, 16, 10) };
    readonly FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
    readonly Label status = Theme.Label("", 9, color: Theme.Muted);
    protected readonly DataGridView Grid = new() { Dock = DockStyle.Fill, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, AllowUserToResizeColumns = false, RowHeadersVisible = false, AutoGenerateColumns = false,
        BackgroundColor = Theme.Surface, BorderStyle = Theme.IsDark ? BorderStyle.None : BorderStyle.FixedSingle, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Theme.Line, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 30, RowTemplate = { Height = 28 }, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing };

    internal string StatusText => status.Text;

    protected ExtraDialog(string title, string note, Size size)
    {
        Text = title; Font = Theme.Font(); BackColor = Theme.Background; ForeColor = Theme.Ink;
        Size = size; MinimumSize = new Size(size.Width, Math.Min(size.Height, SmallestHeight)); StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Absolute, StatusHeight)); layout.RowStyles.Add(new(SizeType.AutoSize));
        Controls.Add(layout);

        Grid.DefaultCellStyle.Font = Theme.Font(10); Grid.DefaultCellStyle.ForeColor = Theme.Ink; Grid.DefaultCellStyle.BackColor = Theme.Surface;
        Grid.DefaultCellStyle.SelectionBackColor = Theme.AccentLight; Grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        Grid.ColumnHeadersDefaultCellStyle.Font = Theme.Font(9, true); Grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Background;
        Grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        Grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Background; Grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Muted;
        layout.Controls.Add(Grid, 0, 1);

        var hint = Theme.Label(note, 9, color: Theme.Muted);
        hint.AutoSize = true; hint.Margin = new Padding(3, 8, 3, 2); hint.MaximumSize = new Size(ClientSize.Width - NoteMargin, 0);
        layout.Controls.Add(hint, 0, 2);
        status.AutoEllipsis = true; layout.Controls.Add(status, 0, 3);

        var close = Theme.Button(T("Cerrar"), true); close.DialogResult = DialogResult.OK;
        buttons.Controls.Add(close);
        layout.Controls.Add(buttons, 0, 4);
        AcceptButton = CancelButton = close; ActiveControl = Grid;
    }

    protected void AddTop(Control control) => layout.Controls.Add(control, 0, 0);
    protected void AddButton(Button button) => buttons.Controls.Add(button);
    protected void Say(string text, bool problem = false) { status.Text = text; status.ForeColor = problem ? Theme.Danger : Theme.Muted; }

    protected void AddText(string header, int width = 0, bool editable = false, int longest = 0)
    {
        var column = new DataGridViewTextBoxColumn { HeaderText = header, ReadOnly = !editable, SortMode = DataGridViewColumnSortMode.NotSortable };
        if (width == 0) { column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; column.MinimumWidth = 120; }
        else column.Width = width;
        if (longest != 0) column.MaxInputLength = longest;
        Grid.Columns.Add(column);
    }

    protected void AddTick(string header, int width) => Grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable });

    protected static void Mark(DataGridViewRow row, bool pending) => row.DefaultCellStyle.ForeColor = pending ? Theme.Accent : Theme.Ink;
}

/// <summary>The names of the boxes of the open save. What changes here stays pending until the game is saved.</summary>
sealed class BoxNamesDialog : ExtraDialog
{
    const int NumberColumn = 0, NameColumn = 1, CountColumn = 2;
    readonly SaveDocument document;
    readonly BoxNameEditor editor;
    readonly Button undo = Theme.Button(T("Deshacer nombres"));
    bool binding;

    public BoxNamesDialog(SaveDocument document, int openBox) : base(T("Nombres de las cajas"),
        T("Cada caja admite hasta {0} caracteres del alfabeto del juego.", document.BoxNames.MaxLength) + "\n" + T("Los cambios se aplican al guardar la partida."), new Size(640, 640))
    {
        this.document = document; editor = document.BoxNames;
        Grid.SelectionMode = DataGridViewSelectionMode.CellSelect; Grid.EditMode = DataGridViewEditMode.EditOnEnter;
        AddText(T("Caja"), 90); AddText(T("Nombre"), editable: true, longest: editor.MaxLength); AddText(T("Pokémon"), 120);
        AddButton(undo);
        binding = true;
        for (int box = 0; box < editor.BoxCount; box++) Grid.Rows.Add("", "", $"{document.BoxSlots(box).Count(p => p != null)} / {document.Save.BoxSlotCount}");
        binding = false;
        for (int box = 0; box < editor.BoxCount; box++) Show(box);
        Grid.CellValueChanged += (_, e) => {
            if (binding || e.RowIndex < 0 || e.ColumnIndex != NameColumn) return;
            editor.Set(e.RowIndex, Grid[NameColumn, e.RowIndex].Value?.ToString() ?? "");
            Show(e.RowIndex); ShowState();
        };
        undo.Click += (_, _) => {
            Grid.CancelEdit(); editor.Reset();
            for (int box = 0; box < editor.BoxCount; box++) Show(box);
            ShowState();
        };
        Load += (_, _) => Grid.CurrentCell = Grid[NameColumn, Math.Clamp(openBox, 0, editor.BoxCount - 1)];
        // A name typed and left without moving away only counts once the cell is left.
        FormClosing += (_, _) => Grid.EndEdit();
        ShowState();
    }

    void Show(int box)
    {
        bool changed = editor.Pending(box) != null;
        binding = true;
        Grid[NumberColumn, box].Value = (box + 1) + (changed ? PendingMark : "");
        Grid[NameColumn, box].Value = editor.Get(box);
        Mark(Grid.Rows[box], changed);
        binding = false;
    }

    void ShowState()
    {
        Say(editor.Validate() ?? "", true);
        undo.Enabled = editor.ChangeCount != 0;
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void RunInteractionChecks(Action<bool, string> check)
    {
        const int box = 1;
        const string wanted = "Mi caja";
        Grid.EndEdit();
        var cell = Grid[NameColumn, box];
        string first = editor.Original(box);
        check(Grid.Rows.Count == document.BoxCount && (string)cell.Value! == first, "The box names window lists every box with its name");
        check(!document.HasChanges && !undo.Enabled, "Opening the box names window stages nothing");
        cell.Value = wanted;
        check(editor.Pending(box) == wanted && document.HasChanges && document.ToolChangeCount == 1 && undo.Enabled, "A new box name stays pending");
        check(Grid[NumberColumn, box].Value!.ToString()!.Contains('●'), "A pending box stands out");
        check(document.BoxLabel(box).StartsWith(wanted) && document.BoxLabel(box).Contains($"/ {document.Save.BoxSlotCount}") && document.Rules.BoxName(box) == first, "The box list shows the pending name, and the open game doesn't change");
        cell.Value = "";
        check(StatusText.Length != 0 && document.ValidateEdits() != null, "An empty box name explains why it can't be saved");
        cell.Value = new string('A', editor.MaxLength + 1);
        check(StatusText.Length != 0 && document.ValidateEdits() != null, "A box name that is too long explains why it can't be saved");
        cell.Value = wanted;
        check(StatusText.Length == 0 && document.ValidateEdits() == null, "A valid box name clears the warning");
        check(EditorView.Clipped(this).Length == 0, "Box names window texts fit");
        undo.PerformClick();
        check(!document.HasChanges && !undo.Enabled && (string)cell.Value! == first, "Undo restores the box names");
    }
}

/// <summary>Who is in the daycare. Taking one out is a box change, so it is undone and saved with the others.</summary>
sealed class DaycareDialog : ExtraDialog
{
    const int StateColumn = 4;
    readonly SaveDocument document;
    readonly DaycareEditor editor;
    readonly int openBox;
    readonly Button withdraw = Theme.Button(T("Sacar a una caja")), stay = Theme.Button(T("Dejar en la guardería"));
    readonly Label egg = Theme.Label("", 10, true, Theme.Accent);

    public DaycareDialog(SaveDocument document, int openBox) : base(T("Guardería"), Note(document), new Size(760, 520))
    {
        this.document = document; this.openBox = openBox; editor = document.Daycare;
        Grid.ReadOnly = true;
        AddText(T("Lugar"), 190); AddText(T("Pokémon")); AddText(T("Nivel"), 70); AddText(T("Objeto"), 150); AddText(T("Estado"), 170);
        AddButton(stay); AddButton(withdraw);
        withdraw.Visible = stay.Visible = editor.CanWithdraw;
        egg.AutoSize = true; egg.Margin = new Padding(3, 2, 3, 8); egg.Visible = editor.HasEggState;
        egg.Text = EggLine(); AddTop(egg);
        foreach (var guest in editor.Guests) Grid.Rows[Grid.Rows.Add(Place(guest), editor.Name(guest), guest.Pokemon.CurrentLevel, Item(guest), "")].Tag = guest;
        Grid.SelectionChanged += (_, _) => ShowSelection();
        withdraw.Click += (_, _) => Act(guest => document.Withdraw(guest, openBox).Message);
        stay.Click += (_, _) => Act(guest => { document.ReturnToDaycare(guest); return T("{0} se queda en la guardería.", editor.Name(guest)); });
        ShowRows();
        if (editor.Guests.Count == 0) Say(T("No hay ningún Pokémon en la guardería."));
    }

    static string Note(SaveDocument document)
    {
        var editor = document.Daycare;
        if (document.Save is SAV8SWSH or SAV7b) return T("En este juego la guardería solo se puede consultar: PKHeX no sabe vaciar sus lugares.");
        return T("Sale como entró, sin la experiencia que haya ganado allí, y llega al primer lugar libre desde la caja abierta.") + "\n" + T("Los cambios se aplican al guardar la partida.");
    }

    string EggLine()
    {
        if (editor.WaitingEgg is { } waiting) return T("Hay un huevo de {0} esperando.", DaycareEditor.SpeciesName(waiting.Species));
        if (editor.DaycareCount == 1) return editor.HasEgg(0) ? T("Hay un huevo esperando.") : T("No hay ningún huevo esperando.");
        return string.Join("  ·  ", Enumerable.Range(0, editor.DaycareCount).Select(i => editor.HasEgg(i) ? T("Guardería {0}: hay un huevo esperando.", i + 1) : T("Guardería {0}: sin huevo.", i + 1)));
    }

    string Place(DaycareGuest guest) => editor.DaycareCount == 1 ? T("Lugar {0}", guest.Slot + 1) : T("Guardería {0} · Lugar {1}", guest.Daycare + 1, guest.Slot + 1);

    string Item(DaycareGuest guest) => guest.Pokemon.HeldItem == 0 ? "" : document.Rules.ItemName(guest.Pokemon.HeldItem);

    DaycareGuest? Selected => Grid.SelectedRows.Count == 0 ? null : (DaycareGuest)Grid.SelectedRows[0].Tag!;

    void Act(Func<DaycareGuest, string> action)
    {
        if (Selected is not { } guest) return;
        try { var message = action(guest); ShowRows(); Say(message); }
        catch (InvalidOperationException ex) { ShowRows(); Say(ex.Message, true); }
    }

    void ShowRows()
    {
        foreach (DataGridViewRow row in Grid.Rows)
        {
            var entry = editor.EntryOf((DaycareGuest)row.Tag!);
            row.Cells[StateColumn].Value = entry != null ? T("Sale a {0}", entry.Location) + PendingMark : T("En la guardería");
            Mark(row, entry != null);
        }
        ShowSelection();
    }

    void ShowSelection()
    {
        bool leaving = Selected is { } guest && editor.IsLeaving(guest);
        withdraw.Enabled = Selected is { } chosen && !leaving && editor.WhyStays(chosen) == null;
        stay.Enabled = leaving;
        if (Selected is { } held && !leaving && editor.WhyStays(held) is { } why) Say(why);
    }

    void Choose(int row) { Grid.ClearSelection(); Grid.Rows[row].Selected = true; }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void RunInteractionChecks(Action<bool, string> check)
    {
        int count = document.Pokemon.Count;
        check(Grid.Rows.Count == editor.Guests.Count && Grid.Rows.Count != 0, "The daycare window lists who is there");
        check(!document.HasChanges && !stay.Enabled, "Opening the daycare window stages nothing");
        Choose(0);
        check(withdraw.Enabled, "A Pokémon that can leave offers to");
        withdraw.PerformClick();
        check(document.Pokemon.Count == count + 1 && document.HasChanges && editor.LeavingCount == 1 && StatusText.Contains(document.Pokemon[^1].Location), "Taking one out stages an arrival and says where it goes");
        check(Grid.Rows[0].Cells[StateColumn].Value!.ToString()!.Contains('●') && !withdraw.Enabled && stay.Enabled, "A Pokémon that leaves stands out and can stay after all");
        check(EditorView.Clipped(this).Length == 0, "Daycare window texts fit");
        stay.PerformClick();
        check(document.Pokemon.Count == count && editor.LeavingCount == 0 && withdraw.Enabled && !Grid.Rows[0].Cells[StateColumn].Value!.ToString()!.Contains('●'), "Staying takes the arrival back");
        document.ResetLayout();
        check(!document.HasChanges, "Nothing is left pending");
    }

    /// <summary>An egg holds its parents back, and says so.</summary>
    internal void RunEggChecks(Action<bool, string> check)
    {
        Choose(0);
        check(egg.Visible && !withdraw.Enabled && StatusText.Length != 0, "With an egg waiting the parents stay, and the window says why");
    }
}

/// <summary>The gifts a Switch game remembers. Clearing a tick forgets one when the game is saved.</summary>
sealed class GiftsDialog : ExtraDialog
{
    const int NameColumn = 2, ReceivedColumn = 3;
    readonly GiftEditor editor;
    readonly Button undo = Theme.Button(T("Deshacer regalos"));
    bool binding;

    public GiftsDialog(GiftEditor editor) : base(T("Regalos recibidos"), Note(editor), new Size(760, 560))
    {
        this.editor = editor;
        AddText(T("N.º de regalo"), 130); AddText(T("Fecha"), 130); AddText(T("Regalo")); AddTick(T("Recibido"), 110);
        foreach (DataGridViewColumn column in Grid.Columns) column.ReadOnly = column.Index != ReceivedColumn;
        AddButton(undo);
        binding = true;
        foreach (var gift in editor.Records)
            Grid.Rows[Grid.Rows.Add(gift.CardId.ToString("0000", CultureInfo.InvariantCulture), gift.Received?.ToString("d", CultureInfo.CurrentCulture) ?? "", "", true)].Tag = gift;
        binding = false;
        // A tick counts when it is made, not when the cell is left.
        Grid.CurrentCellDirtyStateChanged += (_, _) => { if (Grid.IsCurrentCellDirty) Grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        Grid.CellValueChanged += (_, e) => {
            if (binding || e.RowIndex < 0) return;
            var row = Grid.Rows[e.RowIndex];
            editor.SetForgetting((GiftRecord)row.Tag!, !(bool)row.Cells[ReceivedColumn].Value!);
            ShowRows();
        };
        undo.Click += (_, _) => { editor.Reset(); ShowRows(); };
        ShowRows();
        if (editor.Records.Count == 0) Say(T("Esta partida no recuerda ningún regalo."));
    }

    static string Note(GiftEditor editor) => (editor.IsDocumented
        ? T("Quitar la marca borra el regalo de la lista y la señal de recibido, para que el juego pueda entregarlo otra vez.")
        : T("Quitar la marca borra el regalo de la lista. Este registro no está documentado en PKHeX y no se ha comprobado en el juego que baste para recibirlo otra vez."))
        + "\n" + T("Los cambios se aplican al guardar la partida.");

    void ShowRows()
    {
        binding = true;
        foreach (DataGridViewRow row in Grid.Rows)
        {
            var gift = (GiftRecord)row.Tag!;
            bool forgetting = editor.IsForgetting(gift);
            row.Cells[ReceivedColumn].Value = !forgetting;
            row.Cells[NameColumn].Value = editor.Contents(gift) + (forgetting ? PendingMark : "");
            Mark(row, forgetting);
        }
        binding = false;
        undo.Enabled = editor.ChangeCount != 0;
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void RunInteractionChecks(Action<bool, string> check)
    {
        check(Grid.Rows.Count == editor.Records.Count && Grid.Rows.Count != 0 && !undo.Enabled, "The gifts window lists what the save remembers");
        Grid.Rows[0].Cells[ReceivedColumn].Value = false;
        check(editor.ChangeCount == 1 && undo.Enabled && Grid.Rows[0].Cells[NameColumn].Value!.ToString()!.Contains('●'), "Clearing a tick stages the gift, and it stands out");
        check(EditorView.Clipped(this).Length == 0, "Gifts window texts fit");
        undo.PerformClick();
        check(editor.ChangeCount == 0 && !undo.Enabled && (bool)Grid.Rows[0].Cells[ReceivedColumn].Value!, "Undo keeps every gift");
    }
}

/// <summary>The Hall of Fame, to look at.</summary>
sealed class FameDialog : ExtraDialog
{
    public FameDialog(SaveFile save) : base(T("Salón de la Fama"), Note(save), new Size(760, 600))
    {
        Grid.ReadOnly = true;
        AddText(T("Entrada"), 120); AddText(T("Fecha"), 120); AddText(T("Pokémon")); AddText(T("Apodo"), 170); AddText(T("Nivel"), 70);
        var entries = FameRecords.Read(save);
        foreach (var entry in entries)
        {
            string date = entry.Date?.ToString("d", CultureInfo.CurrentCulture) ?? "";
            if (entry.Team.Count == 0) Grid.Rows.Add(entry.Title, date, "", "", "");
            foreach (var member in entry.Team)
                Grid.Rows.Add(entry.Title, date, DaycareEditor.SpeciesName(member.Species) + (member.IsShiny ? " ★" : ""), member.Nickname, member.Level == 0 ? "" : member.Level.ToString(CultureInfo.CurrentCulture));
        }
        if (entries.Count == 0) Say(T("Esta partida aún no tiene entradas."));
    }

    static string Note(SaveFile save) => save switch {
        SAV7 => T("Este juego solo guarda las especies de la primera vez y de la última."),
        SAV8SWSH => T("Este juego solo guarda el día de la primera vez."),
        _ => T("Aquí solo se consulta: nada de esto cambia la partida."),
    };

    internal int RowCount => Grid.Rows.Count;
}

/// <summary>Which raids are on, and their seeds. What changes here stays pending until the game is saved.</summary>
sealed class RaidsDialog : ExtraDialog
{
    const int PlaceColumn = 0, ActiveColumn = 1, SeedColumn = 2, StarsColumn = 3;
    readonly RaidEditor editor;
    readonly ComboBox region = Theme.Combo();
    readonly Button allOn = Theme.Button(T("Activar todas")), allOff = Theme.Button(T("Desactivar todas")), undo = Theme.Button(T("Deshacer incursiones"));
    bool binding;

    public RaidsDialog(RaidEditor editor) : base(T("Incursiones"), T("El juego vuelve a generar las incursiones cuando cambia el día, así que esto dura hasta entonces.") + "\n" + T("Los cambios se aplican al guardar la partida."), new Size(760, 640))
    {
        this.editor = editor;
        Grid.SelectionMode = DataGridViewSelectionMode.CellSelect; Grid.EditMode = DataGridViewEditMode.EditOnEnter;
        AddText(T("Lugar")); Grid.Columns[PlaceColumn].ReadOnly = true;
        AddTick(T("Activa"), 90); AddText(T("Semilla"), 200, editable: true, longest: editor.SeedDigits);
        if (editor.HasStars) AddText(T("Estrellas"), 100, editable: true, longest: 1);
        AddButton(undo); AddButton(allOff); AddButton(allOn);
        region.Items.AddRange(editor.Regions.ToArray<object>());
        region.Margin = new Padding(3, 2, 3, 8); region.Dock = DockStyle.Top; AddTop(region);
        region.SelectedIndexChanged += (_, _) => { Grid.EndEdit(); FillRows(); };
        // A tick counts when it is made. A seed counts when it is finished.
        Grid.CurrentCellDirtyStateChanged += (_, _) => { if (Grid.CurrentCell is DataGridViewCheckBoxCell && Grid.IsCurrentCellDirty) Grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        Grid.CellValueChanged += (_, e) => { if (!binding && e.RowIndex >= 0) Edited(Grid.Rows[e.RowIndex], e.ColumnIndex); };
        allOn.Click += (_, _) => SetAll(true);
        allOff.Click += (_, _) => SetAll(false);
        undo.Click += (_, _) => { Grid.CancelEdit(); editor.Reset(); FillRows(); };
        FormClosing += (_, _) => Grid.EndEdit();
        allOn.Enabled = allOff.Enabled = editor.Spots.Count != 0;
        if (region.Items.Count != 0) region.SelectedIndex = 0;
        else { undo.Enabled = false; region.Visible = false; Say(T("Esta partida aún no tiene incursiones.")); }
    }

    IEnumerable<RaidSpot> InRegion => editor.Spots.Where(s => s.RegionName == (string?)region.SelectedItem);

    void FillRows()
    {
        binding = true;
        Grid.Rows.Clear();
        foreach (var spot in InRegion) Grid.Rows[Grid.Rows.Add()].Tag = spot;
        binding = false;
        foreach (DataGridViewRow row in Grid.Rows) Show(row);
        ShowState();
    }

    void Show(DataGridViewRow row)
    {
        var spot = (RaidSpot)row.Tag!;
        var state = editor.Get(spot);
        bool changed = state != editor.Original(spot);
        binding = true;
        row.Cells[PlaceColumn].Value = spot.Place + (changed ? PendingMark : "");
        row.Cells[ActiveColumn].Value = state.Active;
        row.Cells[SeedColumn].Value = editor.SeedText(spot);
        if (editor.HasStars) row.Cells[StarsColumn].Value = (state.Stars + 1).ToString(CultureInfo.CurrentCulture);
        Mark(row, changed);
        binding = false;
    }

    void ShowState() => undo.Enabled = editor.ChangeCount != 0;

    void Edited(DataGridViewRow row, int column)
    {
        var spot = (RaidSpot)row.Tag!;
        string text = row.Cells[column].Value?.ToString() ?? "";
        string? problem = null;
        if (column == ActiveColumn) editor.SetActive(spot, (bool)row.Cells[column].Value!);
        else if (column == SeedColumn) problem = editor.SetSeed(spot, text);
        else if (column == StarsColumn) problem = editor.SetStars(spot, int.TryParse(text, out int stars) ? stars : 0);
        // What can't be used goes back to what it was, and the line below says why.
        Show(row); ShowState(); Say(problem ?? "", problem != null);
    }

    void SetAll(bool active)
    {
        Grid.EndEdit();
        foreach (var spot in InRegion) editor.SetActive(spot, active);
        foreach (DataGridViewRow row in Grid.Rows) Show(row);
        ShowState();
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void RunInteractionChecks(Action<bool, string> check)
    {
        Grid.EndEdit();
        var spot = (RaidSpot)Grid.Rows[0].Tag!;
        check(Grid.Rows.Count == InRegion.Count() && Grid.Rows.Count != 0 && !undo.Enabled, "The raids window lists the raids of the first region");
        bool active = editor.Get(spot).Active;
        Grid.Rows[0].Cells[ActiveColumn].Value = !active;
        check(editor.Get(spot).Active != active && editor.ChangeCount == 1 && undo.Enabled && Grid.Rows[0].Cells[PlaceColumn].Value!.ToString()!.Contains('●'), "A tick stages the raid, and it stands out");
        Grid.Rows[0].Cells[SeedColumn].Value = "not a seed";
        check(StatusText.Length != 0 && (string)Grid.Rows[0].Cells[SeedColumn].Value! == editor.SeedText(spot), "A bad seed goes back to what it was and says why");
        Grid.Rows[0].Cells[SeedColumn].Value = "BEEF";
        check(StatusText.Length == 0 && editor.Get(spot).Seed == 0xBEEF && (string)Grid.Rows[0].Cells[SeedColumn].Value! == editor.SeedText(spot), "A good seed clears the warning and shows all its digits");
        if (editor.HasStars)
        {
            Grid.Rows[0].Cells[StarsColumn].Value = "9";
            check(StatusText.Length != 0 && editor.Get(spot).Stars != 8, "A number of stars the game hasn't got is refused");
            Grid.Rows[0].Cells[StarsColumn].Value = "5";
            check(editor.Get(spot).Stars == 4, "Stars are shown as the game shows them");
        }
        allOn.PerformClick();
        check(InRegion.All(s => editor.Get(s).Active), "Everything in a region can be turned on at once");
        check(EditorView.Clipped(this).Length == 0, "Raids window texts fit");
        undo.PerformClick();
        check(editor.ChangeCount == 0 && !undo.Enabled && (bool)Grid.Rows[0].Cells[ActiveColumn].Value! == active, "Undo restores the raids");
    }
}

/// <summary>The points of the Pokéathlon. What changes here stays pending until the game is saved.</summary>
sealed class PokeathlonDialog : ExtraDialog
{
    const int ValueColumn = 1, PointsRow = 0;
    readonly PokeathlonEditor editor;
    readonly Button undo = Theme.Button(T("Deshacer puntos"));
    bool binding;

    public PokeathlonDialog(PokeathlonEditor editor) : base(T("Pokéathlon"), T("Son los puntos que se gastan en la tienda del Domo. Llegan hasta {0:N0}.", editor.MaxPoints) + "\n" + T("Los cambios se aplican al guardar la partida."), new Size(560, 330))
    {
        this.editor = editor;
        Grid.SelectionMode = DataGridViewSelectionMode.CellSelect; Grid.EditMode = DataGridViewEditMode.EditOnEnter;
        AddText(T("Dato")); Grid.Columns[0].ReadOnly = true;
        AddText(T("Valor"), 180, editable: true, longest: editor.MaxPoints.ToString(CultureInfo.InvariantCulture).Length + 1);
        AddButton(undo);
        binding = true; Grid.Rows.Add(T("Puntos"), ""); binding = false;
        Grid.CellValueChanged += (_, e) => {
            if (binding || e.RowIndex < 0) return;
            if (uint.TryParse(Grid[ValueColumn, PointsRow].Value?.ToString(), NumberStyles.Integer, CultureInfo.CurrentCulture, out uint points)) editor.Points = points;
            ShowState(editor.Validate() ?? (editor.Points.ToString(CultureInfo.InvariantCulture) == Grid[ValueColumn, PointsRow].Value?.ToString() ? "" : T("Los puntos deben estar entre 0 y {0:N0}.", editor.MaxPoints)));
        };
        undo.Click += (_, _) => { Grid.CancelEdit(); editor.Reset(); ShowState(""); };
        Load += (_, _) => Grid.CurrentCell = Grid[ValueColumn, PointsRow];
        FormClosing += (_, _) => Grid.EndEdit();
        ShowState("");
    }

    void ShowState(string problem)
    {
        binding = true;
        if (problem.Length == 0 || editor.Validate() == null) Grid[ValueColumn, PointsRow].Value = editor.Points.ToString(CultureInfo.InvariantCulture);
        Grid[0, PointsRow].Value = T("Puntos") + (editor.Changed ? PendingMark : "");
        Mark(Grid.Rows[PointsRow], editor.Changed);
        binding = false;
        Say(problem, true);
        undo.Enabled = editor.Changed;
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void RunInteractionChecks(Action<bool, string> check)
    {
        Grid.EndEdit();
        var cell = Grid[ValueColumn, PointsRow];
        check(!undo.Enabled && (string)cell.Value! == editor.Original.ToString(CultureInfo.InvariantCulture), "The Pokéathlon window shows the points");
        cell.Value = "1234";
        check(editor.Points == 1234 && editor.Changed && undo.Enabled, "New points stay pending");
        cell.Value = "letras";
        check(StatusText.Length != 0 && editor.Points == 1234 && (string)cell.Value! == "1234", "Something that isn't a number goes back and says why");
        cell.Value = (editor.MaxPoints + 1).ToString(CultureInfo.InvariantCulture);
        check(StatusText.Length != 0 && editor.Validate() != null, "Too many points explain why they can't be saved");
        check(EditorView.Clipped(this).Length == 0, "Pokéathlon window texts fit");
        undo.PerformClick();
        check(!editor.Changed && !undo.Enabled && StatusText.Length == 0, "Undo restores the points");
    }
}

/// <summary>The secret bases of Hoenn, to look at.</summary>
sealed class SecretBasesDialog : ExtraDialog
{
    public SecretBasesDialog(SaveFile save) : base(T("Bases secretas"), T("Aquí solo se consulta: nada de esto cambia la partida."), new Size(760, 520))
    {
        Grid.ReadOnly = true;
        AddText(T("Propietario"));
        if (save is SAV6AO) { AddText(T("Equipo"), 200); AddText(T("Rango"), 110); }
        AddText(T("Lugar"), 90);
        var bases = SecretBases.Read(save);
        foreach (var entry in bases)
        {
            string place = entry.Location.ToString(CultureInfo.CurrentCulture);
            if (save is SAV6AO) Grid.Rows.Add(entry.Owner, entry.Team, entry.Rank, place);
            else Grid.Rows.Add(entry.Owner, place);
        }
        if (bases.Count == 0) Say(T("Esta partida aún no tiene bases secretas."));
    }

    internal int RowCount => Grid.Rows.Count;
}

/// <summary>The tools that depend on the game, in the Tools menu and in the automatic checks.</summary>
public sealed partial class EditorView
{
    readonly List<(ToolStripItem Item, Func<SaveFile, bool> Offered)> extraTools = [];
    string? extraPictures;

    void BuildExtraTools()
    {
        void Add(string text, Func<SaveFile, bool> offered, Func<SaveDocument, Form> open)
        {
            AddTool(text, () => OpenExtraTool(open));
            extraTools.Add((toolsMenu.Items[^1], offered));
        }
        Add(T("Nombres de las cajas…"), BoxNameEditor.IsKept, d => new BoxNamesDialog(d, box));
        Add(T("Guardería…"), DaycareEditor.IsKept, d => new DaycareDialog(d, box));
        Add(T("Regalos recibidos…"), GiftEditor.IsKept, d => new GiftsDialog(d.Gifts));
        Add(T("Salón de la Fama…"), FameRecords.IsKept, d => new FameDialog(d.Save));
        Add(T("Incursiones…"), RaidEditor.IsKept, d => new RaidsDialog(d.Raids));
        Add(T("Pokéathlon…"), PokeathlonEditor.IsKept, d => new PokeathlonDialog(d.Pokeathlon));
        Add(T("Bases secretas…"), SecretBases.IsKept, d => new SecretBasesDialog(d.Save));
    }

    // A game that doesn't keep something doesn't offer it.
    void ShowExtraTools()
    {
        foreach (var (item, offered) in extraTools) item.Available = document != null && offered(document.Save);
    }

    void OpenExtraTool(Func<SaveDocument, Form> open)
    {
        using (var dialog = open(document!)) dialog.ShowDialog(Window);
        ShowArranged(null);
        if (document!.HasChanges) Notify(T("Cambios preparados. Guarda la partida para aplicarlos."));
        else UpdateState();
    }

    // Saves with something in them, for the windows the open game doesn't have.
    static SaveFile Blank(GameVersion version) => BlankSaveFile.Get(version, "TRAINER", LanguageID.Spanish);

    static PKM Guest(SaveFile save, ushort species, int item)
    {
        var pokemon = save.BlankPKM;
        pokemon.Species = species; pokemon.CurrentLevel = 30; pokemon.Move1 = 1; pokemon.HeldItem = item;
        pokemon.Language = save.Language; pokemon.Nickname = SpeciesName.GetSpeciesName(species, save.Language); pokemon.OriginalTrainerName = save.OT;
        pokemon.ID32 = save.ID32; pokemon.Version = save.Version; pokemon.PID = 0x12345678; pokemon.EncryptionConstant = 0x1357ACE1;
        pokemon.RefreshChecksum();
        return pokemon;
    }

    static SaveDocument DaycareSample(bool egg = false)
    {
        var save = (SAV8BS)Blank(GameVersion.BD);
        save.SetSlotFormatStored(Guest(save, (ushort)Species.Pikachu, 234), save.GetDaycareSlot(0).Span, EntityImportSettings.None);
        save.SetSlotFormatStored(Guest(save, (ushort)Species.Eevee, 0), save.GetDaycareSlot(1).Span, EntityImportSettings.None);
        save.Daycare.IsEggAvailable = egg;
        var path = Path.GetTempFileName();
        try { File.WriteAllBytes(path, save.Write().ToArray()); return SaveDocument.Read(path); }
        finally { File.Delete(path); }
    }

    static GiftEditor GiftSample()
    {
        var save = (SAV8BS)Blank(GameVersion.BD);
        var when = new DateTime(2022, 3, 14, 9, 0, 0, DateTimeKind.Local);
        save.MysteryRecords.SetReceived(0, new RecvData8b(new byte[RecvData8b.SIZE]) { LocalTimestamp = when, DeliveryID = 1001, DataType = 1, Species = (ushort)Species.Manaphy, IsEgg = 1 });
        save.MysteryRecords.SetReceived(1, new RecvData8b(new byte[RecvData8b.SIZE]) { LocalTimestamp = when, DeliveryID = 1002, DataType = 2, Item1 = 1, Item1Count = 5, Item2 = 234, Item2Count = 1, Item3 = 50, Item3Count = 10 });
        save.MysteryRecords.SetReceived(2, new RecvData8b(new byte[RecvData8b.SIZE]) { LocalTimestamp = when, DeliveryID = 1003, DataType = 5, MoneyData = 500_000 });
        return new(new GameRules(save));
    }

    static RaidEditor RaidSample()
    {
        var save = (SAV8SWSH)Blank(GameVersion.SW);
        save.RaidGalar.GetRaid(0).Activate(4, 50, rare: true);
        return new(save);
    }

    static SaveFile BaseSample()
    {
        var save = (SAV6AO)Blank(GameVersion.OR);
        var mine = save.SecretBase.GetSecretBaseSelf();
        mine.BaseLocation = 25; mine.TrainerName = "Ana"; mine.TeamName = "Equipo Rojo"; mine.Rank = SecretBase6Rank.Platinum;
        return save;
    }

    static SaveFile FameSample()
    {
        var save = (SAV6XY)Blank(GameVersion.X);
        for (int visit = 0; visit < HallOfFame6.Entries; visit++)
        {
            var index = new HallFame6Index(save.HallOfFame.GetEntry(visit)[^HallFame6Index.SIZE..]) { HasData = true, ClearIndex = (uint)visit, Year = 16, Month = 2, Day = 27 };
            for (int slot = 0; slot < HallOfFame6.PokeCount; slot++)
            {
                var member = new HallFame6Entity(save.HallOfFame.GetEntity(visit, slot), save.Language) { Species = (ushort)(slot + 1), Level = 100, Nickname = "Ana", IsShiny = slot == 0 };
            }
        }
        return save;
    }

    void CheckExtraTools(Action<bool, string> Check)
    {
        var open = document!;
        bool Offered(string text) => toolsMenu.Items.Cast<ToolStripItem>().First(i => i.Text == text).Available;
        Check(Offered(T("Nombres de las cajas…")) == BoxNameEditor.IsKept(open.Save) && Offered(T("Guardería…")) == DaycareEditor.IsKept(open.Save)
            && Offered(T("Regalos recibidos…")) == GiftEditor.IsKept(open.Save) && Offered(T("Salón de la Fama…")) == FameRecords.IsKept(open.Save), "The Tools menu offers what this game keeps, and nothing else");
        Check(toolsMenu.Items.Cast<ToolStripItem>().Any(i => i.Text == T("Nombres de las cajas…")), "The box names are in the Tools menu");
        using (var dialog = OffScreen(new BoxNamesDialog(open, box))) dialog.RunInteractionChecks(Check);
        open.BoxNames.Set(1, "Mi caja"); ShowArranged(null);
        Check((string)boxPicker.Items[1]! == open.BoxLabel(1) && open.BoxLabel(1).StartsWith("Mi caja"), "The box picker shows the pending name");
        Check(Text.Contains(T(" · {0} cambios pendientes", 1)) && save.Enabled, "A new box name counts in the window title and enables Save");
        open.BoxNames.Reset(); ShowArranged(null);
        Check(!open.HasChanges && !((string)boxPicker.Items[1]!).StartsWith("Mi caja"), "Undoing the names takes them off the box picker");
        var daycare = DaycareSample();
        using (var dialog = OffScreen(new DaycareDialog(daycare, 0))) dialog.RunInteractionChecks(Check);
        using (var dialog = OffScreen(new DaycareDialog(DaycareSample(egg: true), 0))) dialog.RunEggChecks(Check);
        using (var dialog = OffScreen(new GiftsDialog(GiftSample()))) dialog.RunInteractionChecks(Check);
        using (var dialog = OffScreen(new FameDialog(FameSample()))) Check(dialog.RowCount == HallOfFame6.Entries * HallOfFame6.PokeCount, "The Hall of Fame lists every member of every visit");
        using (var dialog = OffScreen(new RaidsDialog(RaidSample()))) dialog.RunInteractionChecks(Check);
        using (var dialog = OffScreen(new PokeathlonDialog(new PokeathlonEditor(Blank(GameVersion.HG))))) dialog.RunInteractionChecks(Check);
        using (var dialog = OffScreen(new SecretBasesDialog(BaseSample()))) Check(dialog.RowCount == 1, "The secret bases window lists the bases in use");
    }

    void CheckExtraLayouts(Action<bool, string> Check, string sizeLabel)
    {
        var windows = new (string Name, Form Window)[] { ("boxnames", new BoxNamesDialog(document!, 0)), ("daycare", new DaycareDialog(DaycareSample(), 0)), ("gifts", new GiftsDialog(GiftSample())), ("fame", new FameDialog(FameSample())), ("raids", new RaidsDialog(RaidSample())), ("pokeathlon", new PokeathlonDialog(new PokeathlonEditor(Blank(GameVersion.HG)))), ("bases", new SecretBasesDialog(BaseSample())) };
        foreach (var (name, dialog) in windows)
        {
            using (OffScreen(dialog))
            {
                foreach (var (label, size) in new[] { ("normal", dialog.Size), ("smallest", dialog.MinimumSize) })
                {
                    dialog.Size = size; dialog.PerformLayout(); Application.DoEvents();
                    var clipped = Clipped(dialog);
                    Check(clipped.Length == 0, $"Every text fits in {dialog.Text} ({sizeLabel}, {label} window) {clipped}");
                    var outside = Descendants(dialog).OfType<Button>().FirstOrDefault(b => b.Visible && (b.Left < 0 || dialog.RectangleToClient(b.RectangleToScreen(b.ClientRectangle)).Right > dialog.ClientSize.Width));
                    Check(outside == null, $"Every button fits in {dialog.Text} ({sizeLabel}, {label} window) {outside?.Text}");
                    var list = Descendants(dialog).OfType<DataGridView>().Single();
                    Check(list.Height > list.ColumnHeadersHeight + list.RowTemplate.Height, $"{dialog.Text} keeps room for its list ({sizeLabel}, {label} window, {list.Height})");
                    if (extraPictures == null) continue;
                    using var picture = Theme.Capture(dialog);
                    picture.Save($"{extraPictures}.{name}-{label}.png", System.Drawing.Imaging.ImageFormat.Png);
                }
                Check(dialog.Width <= Window.MinimumSize.Width && dialog.Height <= Window.MinimumSize.Height, $"{dialog.Text} fits a small screen ({dialog.Size})");
            }
        }
    }
}
