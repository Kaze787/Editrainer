using PKHeX.Core;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>What only Legends: Z-A keeps. Everything here stays prepared, as any other edit, until the save is written.</summary>
sealed class ZaToolsDialog : Form
{
    const int DonutsTab = 0, ClothingTab = 1, PointsTab = 2, ClothingColumns = 2, MostStars = 5;
    readonly SaveDocument document;
    readonly ZaEdits edits;
    bool binding;
    readonly TabControl tabs = new() { Dock = DockStyle.Fill, Font = Theme.Font(10), Padding = new Point(18, 7) };
    readonly Label status = Theme.Label("", 9, color: Theme.Muted);
    readonly Button undo = Theme.Button(T("Deshacer lo preparado")), close = Theme.Button(T("Cerrar"), true);

    readonly Label pocket = Theme.Label("", 10, true);
    readonly DataGridView donuts = new() { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, AutoGenerateColumns = false,
        BackgroundColor = Theme.Surface, BorderStyle = Theme.IsDark ? BorderStyle.None : BorderStyle.FixedSingle, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Theme.Line, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 30, RowTemplate = { Height = 28 }, Margin = new Padding(3, 4, 3, 6) };
    readonly ComboBox[] powers = [Theme.Combo(), Theme.Combo(), Theme.Combo()];
    readonly NumericUpDown amount = new() { Minimum = 1, Maximum = ZaEdits.PocketSize, Value = 10, Width = 90, Font = Theme.Font(10),
        Anchor = AnchorStyles.Left, Margin = new Padding(6, 8, 12, 8) };
    readonly Button addDonuts = Theme.Button(T("Agregar"), true), assortment = Theme.Button(T("Surtido variocolor")), removeDonut = Theme.Button(T("Quitar dónut"));

    readonly List<(ZaClothing Kind, CheckBox Box)> clothing = [];
    readonly Button allClothing = Theme.Button(T("Marcar todo"));

    readonly List<(ZaCounter Counter, NumericUpDown Field)> counters = [];
    readonly Label screwText = Theme.Label("", 10), machineText = Theme.Label("", 10);
    readonly Button collectScrews = Theme.Button(T("Recoger")), collectMachines = Theme.Button(T("Recoger"));

    public ZaToolsDialog(SaveDocument document)
    {
        this.document = document; edits = document.Za!;
        Text = T("Herramientas de Z-A"); Font = Theme.Font(); BackColor = Theme.Background; ForeColor = Theme.Ink;
        Size = new Size(1000, 640); MinimumSize = Size; StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
        BuildDonutsTab(); BuildClothingTab(); BuildPointsTab();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12, 10, 12, 8) };
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 30)); layout.RowStyles.Add(new(SizeType.Absolute, 50));
        layout.Controls.Add(tabs, 0, 0);
        status.AutoEllipsis = true; layout.Controls.Add(status, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        close.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(close); buttons.Controls.Add(undo);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout); CancelButton = close;

        undo.Click += (_, _) => { edits.Reset(); ShowAll(); Say(T("Lo preparado aquí se descartó.")); };
        // What was typed but never left its field still counts.
        FormClosing += (_, _) => { foreach (var (_, field) in counters) _ = field.Value; };
        ShowAll(); Say(T("Nada se escribe hasta que pulses «Guardar partida»."));
    }

    static TableLayoutPanel Page(int rows) => new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, Margin = Padding.Empty };
    static TableLayoutPanel Row(int columns) => new() { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
    static Label Note(string text) => Theme.Label(text, 9, color: Theme.Muted);
    static Label Heading(string text)
    {
        var label = Theme.Label(text, 10, true); label.AutoSize = true; label.Anchor = AnchorStyles.Left; label.Dock = DockStyle.None;
        return label;
    }

    void AddPage(string title, Control content)
    {
        var page = new TabPage(title) { BackColor = Theme.Surface, Padding = new Padding(12, 10, 12, 8) };
        page.Controls.Add(content); tabs.TabPages.Add(page);
    }

    void BuildDonutsTab()
    {
        var page = Page(5);
        page.RowStyles.Add(new(SizeType.Absolute, 30)); page.RowStyles.Add(new(SizeType.Absolute, 40)); page.RowStyles.Add(new(SizeType.Percent, 100));
        page.RowStyles.Add(new(SizeType.Absolute, 44)); page.RowStyles.Add(new(SizeType.Absolute, 50));
        page.Controls.Add(pocket, 0, 0);
        page.Controls.Add(Note(T("Son del DLC Megadimensión. Los nuevos son de cinco estrellas y llevan los poderes que elijas.")), 0, 1);
        DressGrid();
        donuts.Columns.Add(new DataGridViewTextBoxColumn { Name = "Donut", HeaderText = T("Dónut"), Width = 180 });
        donuts.Columns.Add(new DataGridViewTextBoxColumn { Name = "Stars", HeaderText = T("Estrellas"), Width = 80 });
        donuts.Columns.Add(new DataGridViewTextBoxColumn { Name = "Powers", HeaderText = T("Poderes"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 200 });
        page.Controls.Add(donuts, 0, 2);

        var choice = Row(ZaEdits.PowersPerDonut + 1);
        choice.ColumnStyles.Add(new(SizeType.AutoSize));
        var options = new[] { new ZaPower(0, T("— Sin poder —")) }.Concat(ZaEdits.TopPowers()).Cast<object>().ToArray();
        choice.Controls.Add(Heading(T("Poderes")), 0, 0);
        for (int i = 0; i < powers.Length; i++)
        {
            choice.ColumnStyles.Add(new(SizeType.Percent, 100f / powers.Length));
            powers[i].Items.AddRange(options); powers[i].SelectedIndex = 0; powers[i].DropDownWidth = 340;
            powers[i].SelectedIndexChanged += (_, _) => ShowDonutActions();
            choice.Controls.Add(powers[i], i + 1, 0);
        }
        page.Controls.Add(choice, 0, 3);

        var actions = Row(6);
        actions.ColumnStyles.Add(new(SizeType.AutoSize)); actions.ColumnStyles.Add(new(SizeType.AutoSize)); actions.ColumnStyles.Add(new(SizeType.AutoSize));
        actions.ColumnStyles.Add(new(SizeType.AutoSize)); actions.ColumnStyles.Add(new(SizeType.Percent, 100)); actions.ColumnStyles.Add(new(SizeType.AutoSize));
        actions.Controls.Add(Heading(T("Cantidad")), 0, 0); actions.Controls.Add(amount, 1, 0); actions.Controls.Add(addDonuts, 2, 0);
        actions.Controls.Add(assortment, 3, 0); actions.Controls.Add(removeDonut, 5, 0);
        page.Controls.Add(actions, 0, 4);

        addDonuts.Click += (_, _) => EditDonuts(() => T("{0} dónuts preparados.", edits.AddDonuts(powers.Select(p => ((ZaPower)p.SelectedItem!).Hash), (int)amount.Value)));
        assortment.Click += (_, _) => EditDonuts(() => T("{0} dónuts preparados.", edits.AddShinyAssortment((int)amount.Value)));
        removeDonut.Click += (_, _) => EditDonuts(() => {
            if (donuts.CurrentRow?.Tag is not ZaDonut donut) return "";
            edits.RemoveDonut(donut.Slot);
            return T("{0} quitado.", donut.Name);
        });
        donuts.SelectionChanged += (_, _) => { if (!binding) ShowDonutActions(); };
        AddPage(T("Dónuts"), page);
    }

    void DressGrid()
    {
        donuts.DefaultCellStyle.Font = Theme.Font(10); donuts.DefaultCellStyle.ForeColor = Theme.Ink; donuts.DefaultCellStyle.BackColor = Theme.Surface;
        donuts.DefaultCellStyle.SelectionBackColor = Theme.AccentLight; donuts.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        donuts.ColumnHeadersDefaultCellStyle.Font = Theme.Font(9, true); donuts.ColumnHeadersDefaultCellStyle.BackColor = Theme.Background;
        donuts.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        donuts.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Background; donuts.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Muted;
    }

    void BuildClothingTab()
    {
        var page = Page(3);
        page.RowStyles.Add(new(SizeType.Absolute, 62)); page.RowStyles.Add(new(SizeType.Percent, 100)); page.RowStyles.Add(new(SizeType.Absolute, 50));
        page.Controls.Add(Note(T("Marca como compradas las prendas que la partida ya tiene registradas. No agrega otras ni cambia lo que llevas puesto. El peinado y el maquillaje no cambian.")), 0, 0);
        var kinds = edits.Clothing;
        int rows = (kinds.Count + 1) / ClothingColumns;
        var list = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = ClothingColumns, RowCount = rows, Margin = Padding.Empty };
        for (int i = 0; i < ClothingColumns; i++) list.ColumnStyles.Add(new(SizeType.Percent, 100f / ClothingColumns));
        for (int i = 0; i < rows; i++) list.RowStyles.Add(new(SizeType.Percent, 100f / rows));
        foreach (var (kind, index) in kinds.Select((k, i) => (k, i)))
        {
            var box = new CheckBox { Text = T("{0}: tienes {1} de {2}", T(kind.Name), kind.Owned, kind.Known), Dock = DockStyle.Fill, Font = Theme.Font(10), Margin = new Padding(4, 2, 4, 2), Enabled = kind.Missing != 0 };
            box.CheckedChanged += (_, _) => {
                if (binding) return;
                edits.SetUnlocking(kind.Key, box.Checked); ShowSettled();
            };
            clothing.Add((kind, box)); list.Controls.Add(box, index % ClothingColumns, index / ClothingColumns);
        }
        page.Controls.Add(list, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        actions.Controls.Add(allClothing);
        allClothing.Click += (_, _) => {
            bool every = clothing.Where(c => c.Box.Enabled).All(c => c.Box.Checked);
            foreach (var (kind, _) in clothing) edits.SetUnlocking(kind.Key, !every);
            ShowSettled();
        };
        page.Controls.Add(actions, 0, 2);
        AddPage(T("Ropa"), page);
    }

    void BuildPointsTab()
    {
        var page = Page(3);
        page.RowStyles.Add(new(SizeType.AutoSize)); page.RowStyles.Add(new(SizeType.AutoSize)); page.RowStyles.Add(new(SizeType.Percent, 100));
        var numbers = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 14) };
        numbers.ColumnStyles.Add(new(SizeType.AutoSize)); numbers.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var counter in edits.Points)
        {
            var label = Heading(T(counter.Name)); label.Margin = new Padding(3, 8, 18, 8);
            var field = new NumericUpDown { Minimum = 0, Maximum = Math.Max(ZaEdits.MaxPoints, counter.Original), ThousandsSeparator = true, Width = 170,
                Font = Theme.Font(11), Anchor = AnchorStyles.Left, Margin = new Padding(4, 5, 4, 5), Value = counter.Original };
            field.ValueChanged += (_, _) => {
                if (binding) return;
                counter.Value = (uint)field.Value; ShowSettled();
            };
            int row = numbers.RowCount++;
            numbers.RowStyles.Add(new(SizeType.AutoSize));
            numbers.Controls.Add(label, 0, row); numbers.Controls.Add(field, 1, row); counters.Add((counter, field));
        }
        page.Controls.Add(numbers, 0, 0);

        var collection = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
        collection.ColumnStyles.Add(new(SizeType.Percent, 100)); collection.ColumnStyles.Add(new(SizeType.AutoSize));
        for (int i = 0; i < 2; i++) collection.RowStyles.Add(new(SizeType.Absolute, 58));
        collection.RowStyles.Add(new(SizeType.Absolute, 44));
        collection.Controls.Add(screwText, 0, 0); collection.Controls.Add(collectScrews, 1, 0);
        collection.Controls.Add(machineText, 0, 1); collection.Controls.Add(collectMachines, 1, 1);
        var hint = Note(T("Quedan fuera las zonas que aún no visitas: la partida todavía no las tiene."));
        collection.Controls.Add(hint, 0, 2); collection.SetColumnSpan(hint, 2);
        collectScrews.Anchor = collectMachines.Anchor = AnchorStyles.Right;
        page.Controls.Add(collection, 0, 1);
        collectScrews.Click += (_, _) => { edits.SetCollectingScrews(!edits.CollectingScrews); ShowSettled(); };
        collectMachines.Click += (_, _) => {
            try { edits.SetCollectingMachines(!edits.CollectingMachines); ShowSettled(); }
            catch (InvalidOperationException ex) { Say(ex.Message, true); }
        };
        AddPage(T("Puntos y colección"), page);
    }

    void EditDonuts(Func<string> edit)
    {
        try { var message = edit(); ShowAll(); if (message.Length != 0) Say(message); }
        catch (InvalidOperationException ex) { Say(ex.Message, true); }
    }

    void Say(string text, bool problem = false) { status.Text = text; status.ForeColor = problem ? Theme.Danger : Theme.Muted; }

    void ShowAll() { ShowDonuts(); ShowSettled(); }

    // A full pocket has a thousand rows, so it is only filled again when donuts change.
    void ShowSettled()
    {
        binding = true;
        foreach (var (kind, box) in clothing) box.Checked = edits.IsUnlocking(kind.Key);
        var open = clothing.Where(c => c.Box.Enabled).ToList();
        allClothing.Enabled = open.Count != 0;
        allClothing.Text = open.Count != 0 && open.All(c => c.Box.Checked) ? T("Desmarcar todo") : T("Marcar todo");
        foreach (var (counter, field) in counters) field.Value = counter.Value;
        string screw = document.Rules.ItemName(ColorfulScrew9a.ColorfulScrewItemIndex);
        screwText.Text = edits.CollectingScrews ? T("{0}: tendrás {1} al guardar.", screw, edits.ScrewsAfter)
            : T("{0}: tienes {1}. Quedan {2} por recoger.", screw, edits.ScrewsHeld, edits.ScrewsLeft);
        machineText.Text = edits.CollectingMachines ? T("MT del mapa: se recogerán {0} al guardar.", edits.MachinesLeft)
            : T("MT del mapa: quedan {0} por recoger.", edits.MachinesLeft);
        collectScrews.Text = edits.CollectingScrews ? T("No recoger") : T("Recoger");
        collectMachines.Text = edits.CollectingMachines ? T("No recoger") : T("Recoger");
        collectScrews.Enabled = edits.ScrewsLeft != 0; collectMachines.Enabled = edits.MachinesLeft != 0;
        undo.Enabled = edits.HasChanges;
        binding = false;
        ShowDonutActions();
    }

    void ShowDonuts()
    {
        binding = true;
        var list = edits.Donuts();
        donuts.Rows.Clear();
        foreach (var donut in list)
        {
            int row = donuts.Rows.Add(donut.Name, new string('★', Math.Min(donut.Stars, MostStars)), donut.Powers);
            donuts.Rows[row].Tag = donut; donuts.Rows[row].Cells[2].ToolTipText = donut.Powers;
        }
        pocket.Text = edits.HasDonutPocket ? T("Dónuts en el bolsillo: {0} de {1}", list.Count, ZaEdits.PocketSize) : T("Esta partida no tiene bolsillo de dónuts.");
        binding = false;
    }

    void ShowDonutActions()
    {
        bool room = edits.HasDonutPocket && edits.DonutCount < ZaEdits.PocketSize;
        addDonuts.Enabled = room && powers.Any(p => ((ZaPower)p.SelectedItem!).Hash != 0);
        addDonuts.BackColor = addDonuts.Enabled ? Theme.Accent : Theme.Line;
        assortment.Enabled = room;
        removeDonut.Enabled = donuts.CurrentRow?.Tag is ZaDonut;
    }

    /// <summary>For the screenshots, which can't wait on a dialog.</summary>
    internal static void SaveImage(SaveDocument document, string path, int tab)
    {
        document.Za!.AddDonuts([ZaEdits.TopPowers()[0].Hash, ZaEdits.TopPowers()[1].Hash, ZaEdits.TopPowers()[2].Hash], 3);
        document.Za.AddShinyAssortment(4);
        using var dialog = new ZaToolsDialog(document) { StartPosition = FormStartPosition.Manual, Location = new Point(-16000, -16000) };
        dialog.Show(); dialog.tabs.SelectedIndex = tab; dialog.Refresh();
        using var bitmap = Theme.Capture(dialog);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        dialog.Close();
    }

    // Each control inside the page it belongs to, so nothing ends up out of reach at the smallest size.
    static string OutOfReach(Control page) => string.Join(" | ", Descendants(page).Where(c => {
        if (!c.Visible) return false;
        var area = page.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
        return area.Right > page.ClientSize.Width || area.Bottom > page.ClientSize.Height;
    }).Select(c => $"{c.GetType().Name} {c.Text.Split('\n')[0]}"));

    static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));

    /// <summary>What a user does here, for the automatic checks, in any language and on any save.</summary>
    internal void RunChecks(Action<bool, string> check, Func<Control, string> clipped)
    {
        void Open(int tab) { tabs.SelectedIndex = tab; PerformLayout(); Application.DoEvents(); }
        void Fits(string when)
        {
            for (int tab = 0; tab < tabs.TabCount; tab++)
            {
                Open(tab);
                string cut = clipped(this), away = OutOfReach(tabs.TabPages[tab]);
                check(cut.Length == 0, $"Every text fits in the Z-A tools ({when}, tab {tab}) {cut}");
                check(away.Length == 0, $"Nothing leaves the page in the Z-A tools ({when}, tab {tab}) {away}");
            }
        }
        check(!document.HasChanges && !undo.Enabled, "The Z-A tools open with nothing prepared");
        check(tabs.TabCount == 3 && counters.Count == edits.Points.Count && clothing.Count == 10, "The Z-A tools show donuts, clothing and points");
        Fits("as it opens");

        Open(DonutsTab);
        int start = donuts.Rows.Count;
        check(pocket.Text.Contains(start.ToString()), "The pocket shows how many donuts there are");
        check(!addDonuts.Enabled, "Adding donuts needs a power first");
        powers[0].SelectedIndex = 1; powers[1].SelectedIndex = 2; powers[2].SelectedIndex = 3; amount.Value = 2;
        check(addDonuts.Enabled, "Choosing a power enables the button");
        addDonuts.PerformClick();
        check(donuts.Rows.Count == start + 2 && edits.DonutsChanged && document.HasChanges && undo.Enabled, "Add prepares donuts in the list and as a pending change");
        check(donuts.Rows[start].Cells[2].Value?.ToString()?.Split(" · ").Length == 3, "The list names the three powers");
        powers[1].SelectedIndex = 1;
        addDonuts.PerformClick();
        check(donuts.Rows.Count == start + 2 && status.ForeColor == Theme.Danger && status.Text.Length != 0, "A repeated power is refused and explained");
        powers[1].SelectedIndex = 0; powers[2].SelectedIndex = 0;
        amount.Value = 1; assortment.PerformClick();
        check(donuts.Rows.Count == start + 3, "The shiny assortment adds donuts");
        donuts.CurrentCell = donuts.Rows[start].Cells[0];
        removeDonut.PerformClick();
        check(donuts.Rows.Count == start + 2 && removeDonut.Enabled, "Remove takes the selected donut out");

        Open(ClothingTab);
        var open = clothing.Where(c => c.Box.Enabled).ToList();
        if (open.Count == 0) check(!allClothing.Enabled, "Nothing to mark leaves the clothing button off");
        else
        {
            allClothing.PerformClick();
            check(open.All(c => c.Box.Checked && edits.IsUnlocking(c.Kind.Key)), "Mark all prepares every kind that has something to buy");
            open[0].Box.Checked = false;
            check(!edits.IsUnlocking(open[0].Kind.Key) && edits.Clothing.Count == clothing.Count, "Unticking a kind leaves it out");
        }
        check(clothing.Where(c => !c.Box.Enabled).All(c => !c.Box.Checked), "A kind with nothing to buy can't be marked");

        Open(PointsTab);
        var (counter, field) = counters[0];
        field.Value = field.Value == 4321 ? 1234 : 4321;
        check(counter.Changed && counter.Value == (uint)field.Value, "Changing the points prepares them");
        check(collectScrews.Enabled == (edits.ScrewsLeft != 0) && collectMachines.Enabled == (edits.MachinesLeft != 0), "Collecting is only on when something is left");
        if (collectScrews.Enabled)
        {
            collectScrews.PerformClick();
            check(edits.CollectingScrews && screwText.Text.Contains(edits.ScrewsAfter.ToString()), "Collecting the screws says how many there will be");
        }
        if (collectMachines.Enabled)
        {
            collectMachines.PerformClick();
            check(edits.CollectingMachines && edits.MachineItems.All(id => document.GetItemCount(id) == 1), "Collecting the machines puts them in the bag");
        }
        Fits("with everything prepared");
        Open(PointsTab);
        undo.PerformClick();
        check(!document.HasChanges && !undo.Enabled && donuts.Rows.Count == start && field.Value == counter.Original, "Undo leaves nothing prepared");
        field.Text = "777"; Close();
        check(counter.Value == 777, "Points typed but not confirmed still count on closing");
        edits.Reset();
    }
}
