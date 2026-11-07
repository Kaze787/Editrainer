using static Editrainer.Strings;

namespace Editrainer;

sealed class ResourcesPanel : UserControl
{
    // Categories stay in Spanish inside the document, which is what makes them safe to compare.
    sealed record Category(string Key)
    {
        public override string ToString() => T(Key);
    }

    const string AllCategories = "Todos";
    static readonly string[] CategoryOrder = [GameRules.ExperienceCategory, "Medicina", "Poké Balls", "Bayas", "Objetos", "MT", "Megapiedras", "Tesoros"];
    SaveDocument? document;
    bool binding;
    readonly Button experience = Theme.Button(T("Ver caramelos"));
    readonly NumericUpDown money = new() { Minimum = 0, Maximum = 9_999_999, ThousandsSeparator = true, Width = 190,
        Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = Theme.Font(12), Margin = new Padding(4, 7, 12, 7) };
    readonly TextBox search = new() { PlaceholderText = T("Buscar objeto: caramelo, poción…"), Dock = DockStyle.Fill, Font = Theme.Font(), Margin = new Padding(3, 8, 12, 7) };
    readonly ComboBox category = Theme.Combo();
    readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, AutoGenerateColumns = false,
        BackgroundColor = Theme.Surface, BorderStyle = Theme.IsDark ? BorderStyle.None : BorderStyle.FixedSingle, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Theme.Line, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 30, RowTemplate = { Height = 30 },
        Margin = new Padding(3, 6, 3, 8) };
    readonly NumericUpDown amount = new() { Minimum = 0, Maximum = 999, Value = 10, Width = 100,
        Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = Theme.Font(10), Margin = new Padding(6, 8, 12, 8) };
    readonly Label selectedName = Theme.Label(T("Selecciona un objeto de la lista"), 10, true);
    readonly Label hint = Theme.Label(T("Los cambios se aplican al guardar la partida."), 9, color: Theme.Muted);
    readonly Button add = Theme.Button(T("+ Agregar"), true), set = Theme.Button(T("Fijar cantidad")), reset = Theme.Button(T("Deshacer dinero y objetos"));
    public event EventHandler? DraftChanged;
    public event Action<string>? StatusMessage;

    public ResourcesPanel()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Surface;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(16, 10, 16, 8) };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 32));
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Absolute, 52)); Controls.Add(layout);
        var wallet = Row(); wallet.ColumnCount = 4;
        wallet.ColumnStyles.Add(new(SizeType.AutoSize)); wallet.ColumnStyles.Add(new(SizeType.Percent, 100));
        wallet.ColumnStyles.Add(new(SizeType.AutoSize)); wallet.ColumnStyles.Add(new(SizeType.AutoSize));
        var moneyLabel = Theme.Label(T("Dinero"), 10, true); moneyLabel.AutoSize = true;
        wallet.Controls.Add(moneyLabel, 0, 0); wallet.Controls.Add(money, 1, 0);
        var max = Theme.Button(T("Máximo")); wallet.Controls.Add(max, 2, 0); wallet.Controls.Add(reset, 3, 0);
        layout.Controls.Add(wallet, 0, 0);

        var filters = Row(); filters.ColumnCount = 3;
        filters.ColumnStyles.Add(new(SizeType.Percent, 100)); filters.ColumnStyles.Add(new(SizeType.Absolute, 165)); filters.ColumnStyles.Add(new(SizeType.AutoSize));
        filters.Controls.Add(search, 0, 0);
        category.Items.Add(new Category(AllCategories)); category.SelectedIndex = 0;
        filters.Controls.Add(category, 1, 0);
        filters.Controls.Add(experience, 2, 0); layout.Controls.Add(filters, 0, 1);

        grid.DefaultCellStyle.Font = Theme.Font(10); grid.DefaultCellStyle.ForeColor = Theme.Ink; grid.DefaultCellStyle.BackColor = Theme.Surface;
        grid.DefaultCellStyle.SelectionBackColor = Theme.AccentLight; grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = Theme.Font(9, true); grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Background;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Background;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Muted;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = T("Objeto"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 165 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = T("Categoría"), Width = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Count", HeaderText = T("Cantidad"), Width = 87, DefaultCellStyle = new() { Alignment = DataGridViewContentAlignment.MiddleCenter } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Max", HeaderText = T("Máximo"), Width = 80, DefaultCellStyle = new() { Alignment = DataGridViewContentAlignment.MiddleCenter } });
        layout.Controls.Add(grid, 0, 2); layout.Controls.Add(selectedName, 0, 3);

        var edit = Row(); edit.ColumnCount = 5;
        edit.ColumnStyles.Add(new(SizeType.AutoSize)); edit.ColumnStyles.Add(new(SizeType.Absolute, 110));
        edit.ColumnStyles.Add(new(SizeType.AutoSize)); edit.ColumnStyles.Add(new(SizeType.AutoSize)); edit.ColumnStyles.Add(new(SizeType.Percent, 100));
        var amountLabel = Theme.Label(T("Cantidad"), 10, true); amountLabel.AutoSize = true;
        edit.Controls.Add(amountLabel, 0, 0); edit.Controls.Add(amount, 1, 0); edit.Controls.Add(add, 2, 0); edit.Controls.Add(set, 3, 0);
        layout.Controls.Add(edit, 0, 4); layout.Controls.Add(hint, 0, 5);

        money.ValueChanged += (_, _) => { if (!binding && document != null) { document.Money = (long)money.Value; Changed(); } };
        max.Click += (_, _) => { if (document != null) money.Value = document.Rules.MaxMoney; };
        search.TextChanged += (_, _) => RefreshItems(); category.SelectedIndexChanged += (_, _) => RefreshItems();
        experience.Click += (_, _) => { search.Text = ""; category.SelectedItem = new Category(GameRules.ExperienceCategory); };
        grid.SelectionChanged += (_, _) => { if (!binding) BindSelection(); };
        add.Click += (_, _) => EditQuantity(true); set.Click += (_, _) => EditQuantity(false);
        reset.Click += (_, _) => {
            if (document == null) return;
            try { document.ResetResources(); Bind(document); Changed(); StatusMessage?.Invoke(T("Cambios de dinero y objetos descartados.")); }
            catch (InvalidOperationException ex) { StatusMessage?.Invoke(ex.Message); }
        };
        Enabled = false;
    }

    static TableLayoutPanel Row() => new() { Dock = DockStyle.Fill, AutoSize = true, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
    BagItem? Selected => grid.CurrentRow?.Tag as BagItem;

    public void Bind(SaveDocument doc)
    {
        bool newGame = document != doc;
        document = doc; binding = true; Enabled = true; money.Maximum = doc.Rules.MaxMoney; money.Value = Math.Min(doc.Money, doc.Rules.MaxMoney);
        if (newGame) FillCategories(doc);
        reset.Enabled = doc.ResourceChangeCount != 0; binding = false; RefreshItems();
    }

    // Each game has its own pouches, so the filter only offers the ones it uses.
    void FillCategories(SaveDocument doc)
    {
        var used = doc.Items.Select(i => i.Category).Distinct().ToList();
        var ordered = CategoryOrder.Where(used.Contains).Concat(used.Except(CategoryOrder)).ToList();
        category.Items.Clear(); category.Items.Add(new Category(AllCategories)); category.Items.AddRange(ordered.Select(key => new Category(key)).ToArray<object>());
        category.SelectedIndex = 0; experience.Visible = used.Contains(GameRules.ExperienceCategory);
    }

    void Changed() { reset.Enabled = document?.ResourceChangeCount > 0; DraftChanged?.Invoke(this, EventArgs.Empty); }

    internal void RefreshDraft() { if (document != null) { RefreshItems(); reset.Enabled = document.ResourceChangeCount != 0; } }
    internal void ShowItem(ushort id)
    {
        category.SelectedIndex = 0; search.Text = document?.Items.FirstOrDefault(i => i.Id == id)?.Name ?? ""; RefreshItems();
    }

    void RefreshItems()
    {
        if (document == null) return;
        ushort? keepId = Selected?.Id; binding = true; grid.Rows.Clear();
        string query = search.Text.Trim(), filter = (category.SelectedItem as Category)?.Key ?? AllCategories;
        foreach (var item in document.Items.Where(i => filter == AllCategories || i.Category == filter)
            .Where(i => query.Length == 0 || System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(i.Name, query,
                System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0))
        {
            int count = document.GetItemCount(item.Id);
            var row = grid.Rows[grid.Rows.Add(item.Name + (count != item.OriginalCount ? "  ●" : ""), T(item.Category), count, item.MaxCount)];
            row.Tag = item; row.Cells[0].ToolTipText = item.Name;
            if (count != item.OriginalCount) row.DefaultCellStyle.ForeColor = Theme.Accent;
        }
        grid.ClearSelection();
        var selectedRow = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ((BagItem)r.Tag!).Id == keepId) ?? grid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
        if (selectedRow != null) { grid.CurrentCell = selectedRow.Cells[0]; selectedRow.Selected = true; }
        binding = false; BindSelection();
    }

    void BindSelection()
    {
        var item = Selected; bool editable = item != null && document != null && !document.IsItemHeld(item.Id);
        amount.Enabled = add.Enabled = set.Enabled = editable;
        if (item == null || document == null) { selectedName.Text = T("No hay objetos con ese filtro."); hint.Text = T("Prueba con otro nombre o categoría."); return; }
        amount.Maximum = item.MaxCount; if (amount.Value > amount.Maximum) amount.Value = amount.Maximum;
        selectedName.Text = item.Name;
        hint.Text = T("En la partida: {0}  ·  Cantidad preparada: {1}  ·  Máximo: {2}", item.OriginalCount, document.GetItemCount(item.Id), item.MaxCount) + "\n"
            + (document.IsItemHeld(item.Id) ? T("Este objeto está equipado en un Pokémon.") : T("Agregar suma a lo que tienes. Fijar cantidad establece el total; 0 lo retira de la bolsa."));
    }

    void EditQuantity(bool increment)
    {
        if (document == null || Selected is not { } item) return;
        try
        {
            if (increment)
            {
                int added = document.AddItem(item.Id, (int)amount.Value);
                StatusMessage?.Invoke(added == 0 ? T("No se agregaron unidades; revisa la cantidad y el máximo.") : T("{0}: agregadas {1} unidades.", item.Name, added));
            }
            else { document.SetItemCount(item.Id, (int)amount.Value); StatusMessage?.Invoke(T("{0}: cantidad preparada {1}.", item.Name, amount.Value)); }
            RefreshItems(); Changed();
        }
        catch (InvalidOperationException ex) { StatusMessage?.Invoke(ex.Message); }
    }

    internal int VisibleItemRows => grid.DisplayedRowCount(false);

    internal void RunInteractionChecks(Action<bool, string> check)
    {
        if (document == null) throw new InvalidOperationException("No hay partida cargada.");
        check(money.Value == document.Save.Money && grid.Rows.Count == document.Items.Count, "Resources tab loads money and the item catalog");
        money.Value = document.Save.Money == 123456 ? 234567 : 123456;
        check(document.Money == (long)money.Value && document.MoneyChanged && document.HasChanges, "Money edit stages a resource change");
        search.Text = "caramelo";
        check(grid.Rows.Count >= 6 && grid.Rows.Cast<DataGridViewRow>().All(r => ((BagItem)r.Tag!).IsExperience), "Item search finds experience and Rare Candies");
        category.SelectedIndex = 1;
        var row = grid.Rows.Cast<DataGridViewRow>().Single(r => ((BagItem)r.Tag!).Id == 1128); grid.CurrentCell = row.Cells[0];
        int start = document.GetItemCount(1128); amount.Value = 15; add.PerformClick();
        check(document.GetItemCount(1128) == Math.Min(start + 15, 999), "Add button increases Exp. Candy XL quantity");
        amount.Value = 40; set.PerformClick();
        check(document.GetItemCount(1128) == 40, "Set quantity button sets an absolute item count");
        amount.Value = 0; set.PerformClick(); check(document.GetItemCount(1128) == 0, "Quantity zero removes an item");
        reset.PerformClick(); check(document.ResourceChangeCount == 0 && money.Value == document.Save.Money, "Undo resets money and item edits together");
        search.Text = "objeto-inexistente";
        check(grid.Rows.Count == 0 && !add.Enabled && !set.Enabled, "Empty item search disables quantity actions");
        search.Text = ""; category.SelectedIndex = 0;
    }
}
