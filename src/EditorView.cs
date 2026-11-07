using PKHeX.Core;
using System.Diagnostics;
using System.Text.Json;
using PKMove = PKHeX.Core.Move;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>Everything inside the window. Texts and colours are set while it is built, so new settings mean a new view.</summary>
public sealed partial class EditorView : UserControl
{
    const string AppVersion = "2.2";
    const int SidebarWidth = 340, TeamTitleRow = 2, TeamTitleHeight = 26, TeamGridRow = 3, TeamGridHeight = 72, HeroHeight = 172, BagTab = 2, DetailsTab = 3;

    /// <summary>What the view that replaces this one needs to carry on where it left off.</summary>
    public sealed record Snapshot(SaveDocument? Document, PokemonEntry? Entry, int Box, string Search, int Tab);
    string AppTitle => BrandName + " · " + AppVersion;
    string BrandName => document == null ? "Editrainer" : "Editrainer " + document.Rules.ShortName;
    readonly string appRoot;
    readonly SpriteStore sprites;
    SaveDocument? document;
    PokemonEntry? current;
    bool binding;
    readonly TextBox search = new() { PlaceholderText = T("Buscar Pokémon…"), Dock = DockStyle.Fill, Font = Theme.Font(), BorderStyle = BorderStyle.FixedSingle };
    readonly ComboBox boxPicker = Theme.Combo(), nature = Theme.Combo(), target = Theme.Combo(), formPicker = Theme.Combo(), abilityPicker = Theme.Combo();
    readonly SlotGrid teamGrid, boxGrid;
    readonly TableLayoutPanel sidebar = Table(1, 6);
    readonly Button previousBox = BoxButton("◀"), nextBox = BoxButton("▶");
    int box;
    List<PokemonEntry> shown = [];
    readonly NumericUpDown[] ivs = new NumericUpDown[6], evs = new NumericUpDown[6];
    readonly Label[] baseStats = new Label[6], resultingStats = new Label[6];
    readonly MovePicker[] moves = new MovePicker[4];
    readonly CheckBox shiny = new() { Text = T("Shiny / variocolor"), AutoSize = true, Font = Theme.Font(9, true), ForeColor = Theme.Accent, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 6, 6) };
    readonly Label trainer = Theme.Label(T("Abre tu partida para empezar"), 9, color: Theme.Muted);
    readonly Label count = Theme.Label(T("TUS POKÉMON"), 9, true, Theme.Muted);
    readonly Label heroName = Theme.Label(T("Tu equipo, a tu manera"), 22, true);
    readonly Label heroSpecies = Theme.Label(T("IV, EV, naturalezas y movimientos"), 11, color: Theme.Muted);
    readonly Label heroTypes = Theme.Label(T("Todos los juegos de Pokémon"), 10, true, Theme.Accent);
    readonly Label brand = Theme.Label("Editrainer", 23, true);
    readonly Label formLabel = Theme.Label(T("Forma"), 9, true);
    readonly Label teamTitle = Theme.Label(T("Equipo"), 10, true);
    readonly Label ivHeading = Theme.Label($"{T("IV")} · 0–31", 9, true, Theme.Muted), evHeading = Theme.Label($"{T("EV")} · 0–252", 9, true, Theme.Muted);
    readonly Label[] statLabels = new Label[6];
    readonly Button maxIV = Theme.Button(T("{0} a {1}", T("IV"), 31));
    readonly PictureBox portrait = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(8) };
    readonly Label natureEffect = Theme.Label("", 10, true, Theme.Accent);
    readonly Label abilityLabel = Theme.Label(T("Habilidad"), 10, true), abilityNote = Theme.Label("", 9, color: Theme.Muted);
    readonly Label evTotal = Theme.Label("EV: 0 / 510", 9, true);
    readonly Label buildHint = Theme.Label(T("Elige una evolución y pulsa Optimizar Pokémon."), 9, color: Theme.Muted);
    readonly Label validation = Theme.Label("", 9, color: Theme.Danger);
    readonly Label status = Theme.Label(T("Los cambios se guardan cuando tú lo decidas."), 9, color: Theme.Muted);
    readonly Label moveHint = Theme.Label(T("Elige entre los movimientos compatibles de este Pokémon."), 10, color: Theme.Muted);
    readonly Button save = Theme.Button(T("Guardar partida"), true);
    readonly Button saveAs = Theme.Button(T("Guardar como…"));
    readonly Button optimizeAll = Theme.Button(T("Optimizar todos"));
    readonly Button restoreSelected = Theme.Button(T("Deshacer este Pokémon"));
    readonly TabControl tabs = new() { Dock = DockStyle.Fill, Font = Theme.Font(10), Padding = new Point(18, 7) };
    readonly Panel editor = new() { Dock = DockStyle.Fill };
    readonly ToolTip tips = new() { AutoPopDelay = 12000, InitialDelay = 350 };
    readonly TextBox nicknameBox = DetailBox(), trainerNameBox = DetailBox();
    readonly NumericUpDown levelBox = Number(Experience.MaxLevel);
    readonly ComboBox ballPicker = Theme.Combo();
    readonly Label ballLabel = FieldLabel(T("Poké Ball"));
    readonly Label legalityVerdict = Theme.Label("", 11, true);
    readonly TextBox legalityReport = new() {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Theme.Font(9),
        BackColor = Theme.Surface, ForeColor = Theme.Ink, BorderStyle = BorderStyle.None, Margin = new Padding(3, 0, 3, 6),
    };
    readonly ContextMenuStrip toolsMenu = new() { Font = Theme.Font() };
    readonly Button toolsButton = Theme.Button(T("Herramientas"));
    ToolStripItem? zaTool;
    readonly ResourcesPanel resources = new();
    readonly EquipmentPanel equipment = new();
    TableLayoutPanel? detailsLayout, heroPanel;
    ToolWindow? tool;
    string? scratchBackups;   // the automatic checks keep their hands off the user's own folder
    string BackupRoot => scratchBackups ?? Path.Combine(appRoot, "Backups");

    readonly GearButton settingsButton = new();

    public event Action? SettingsRequested;
    Form Window => FindForm()!;

    internal EditorView(string root, SpriteStore spriteStore)
    {
        appRoot = root; sprites = spriteStore;
        teamGrid = new SlotGrid(sprites, tips, showsLevel: true);
        boxGrid = new SlotGrid(sprites, tips, emptyText: T("Ningún Pokémon de las cajas coincide."));
        Text = AppTitle; Font = Theme.Font(); BackColor = Theme.Background;
        ForeColor = Theme.Ink; Dock = DockStyle.Fill;
        BuildLayout(); WireEvents(); WireBoxes();
        AllowDrop = true;
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && CanDiscard()) LoadFile(files[0]); };
        UpdateState();
    }

    static TableLayoutPanel Table(int columns, int rows) => new() {
        Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty, Padding = Padding.Empty,
    };
    static Button BoxButton(string text)
    {
        var button = Theme.Button(text); button.AutoSize = false; button.MinimumSize = Size.Empty; button.Padding = Padding.Empty;
        button.Dock = DockStyle.Fill; button.Margin = new Padding(0, 4, 0, 4); button.Enabled = false;
        return button;
    }
    static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
    static TextBox DetailBox() => new() { Dock = DockStyle.Fill, Font = Theme.Font(10), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 6, 4, 4) };
    static Label FieldLabel(string text)
    {
        var label = Theme.Label(text, 10, true); label.AutoSize = true; label.Margin = new Padding(3, 10, 12, 10);
        return label;
    }

    void BuildLayout()
    {
        var root = Table(1, 4); root.Padding = new Padding(22, 14, 22, 12);
        root.RowStyles.Add(new(SizeType.Absolute, 58)); root.RowStyles.Add(new(SizeType.Absolute, 32));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 78));
        Controls.Add(root);
        var header = Table(3, 1); header.ColumnStyles.Add(new(SizeType.Absolute, 54)); header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.Absolute, 500));
        settingsButton.Size = new Size(44, 44); settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Left; settingsButton.Margin = new Padding(0, 7, 10, 0);
        settingsButton.AccessibleName = T("Configuración");
        settingsButton.Click += (_, _) => SettingsRequested?.Invoke(); tips.SetToolTip(settingsButton, T("Configuración"));
        header.Controls.Add(settingsButton, 0, 0);
        var brandPanel = Table(1, 2); brandPanel.RowStyles.Add(new(SizeType.Percent, 68)); brandPanel.RowStyles.Add(new(SizeType.Percent, 32));
        brandPanel.Controls.Add(brand, 0, 0);
        brandPanel.Controls.Add(Theme.Label(T("PREPARA TU PRÓXIMO COMBATE"), 8, true, Theme.Accent), 0, 1);
        header.Controls.Add(brandPanel, 1, 0);
        var fileActions = Flow(); fileActions.FlowDirection = FlowDirection.RightToLeft;
        var open = Theme.Button(T("Abrir partida…")); open.Click += (_, _) => OpenDialog();
        var find = Theme.Button(T("Buscar partidas")); find.Click += async (_, _) => await FindSaves();
        fileActions.Controls.Add(open); fileActions.Controls.Add(find);
        BuildTools();
        if (toolsMenu.Items.Count != 0)
        {
            toolsButton.Click += (_, _) => toolsMenu.Show(toolsButton, new Point(0, toolsButton.Height));
            fileActions.Controls.Add(toolsButton);
        }
        header.Controls.Add(fileActions, 2, 0); root.Controls.Add(header, 0, 0);
        trainer.AutoEllipsis = true; root.Controls.Add(trainer, 0, 1);

        var body = Table(2, 1); body.ColumnStyles.Add(new(SizeType.Absolute, SidebarWidth)); body.ColumnStyles.Add(new(SizeType.Percent, 100));
        sidebar.BackColor = Theme.Surface; sidebar.Padding = new Padding(12);
        sidebar.Margin = new Padding(0, 6, 16, 0);
        sidebar.RowStyles.Add(new(SizeType.Absolute, 30)); sidebar.RowStyles.Add(new(SizeType.Absolute, 35));
        sidebar.RowStyles.Add(new(SizeType.Absolute, TeamTitleHeight)); sidebar.RowStyles.Add(new(SizeType.Absolute, TeamGridHeight));
        sidebar.RowStyles.Add(new(SizeType.Absolute, 42)); sidebar.RowStyles.Add(new(SizeType.Percent, 100));
        sidebar.Controls.Add(count, 0, 0); sidebar.Controls.Add(search, 0, 1);
        teamTitle.TextAlign = ContentAlignment.MiddleCenter; teamTitle.Margin = Padding.Empty; sidebar.Controls.Add(teamTitle, 0, TeamTitleRow); sidebar.Controls.Add(teamGrid, 0, TeamGridRow);
        var boxRow = Table(3, 1); boxRow.ColumnStyles.Add(new(SizeType.Absolute, 40)); boxRow.ColumnStyles.Add(new(SizeType.Percent, 100)); boxRow.ColumnStyles.Add(new(SizeType.Absolute, 40));
        boxPicker.Enabled = false; boxRow.Controls.Add(previousBox, 0, 0); boxRow.Controls.Add(boxPicker, 1, 0); boxRow.Controls.Add(nextBox, 2, 0);
        sidebar.Controls.Add(boxRow, 0, 4); sidebar.Controls.Add(boxGrid, 0, 5); body.Controls.Add(sidebar, 0, 0);
        teamGrid.SetSlots(new PokemonEntry?[SaveDocument.TeamSize], SaveDocument.TeamSize);
        boxGrid.SetSlots(new PokemonEntry?[30], 6);

        var right = Table(1, 3); detailsLayout = right; right.Margin = new Padding(0, 6, 0, 0); right.BackColor = Theme.Surface;
        right.RowStyles.Add(new(SizeType.Absolute, HeroHeight)); right.RowStyles.Add(new(SizeType.Percent, 100)); right.RowStyles.Add(new(SizeType.Absolute, 33));
        var hero = Table(2, 1); heroPanel = hero; hero.Padding = new Padding(14, 4, 16, 0);
        hero.ColumnStyles.Add(new(SizeType.Absolute, 164)); hero.ColumnStyles.Add(new(SizeType.Percent, 100));
        portrait.BackColor = Theme.AccentLight; hero.Controls.Add(portrait, 0, 0);
        var identity = Table(1, 4); identity.Padding = new Padding(12, 4, 0, 4);
        identity.RowStyles.Add(new(SizeType.Absolute, 56)); identity.RowStyles.Add(new(SizeType.Absolute, 31));
        identity.RowStyles.Add(new(SizeType.Absolute, 30)); identity.RowStyles.Add(new(SizeType.Percent, 100));
        identity.Controls.Add(heroName, 0, 0); identity.Controls.Add(heroSpecies, 0, 1); identity.Controls.Add(heroTypes, 0, 2);
        var appearanceActions = Table(4, 1);
        for (int i = 0; i < 3; i++) appearanceActions.ColumnStyles.Add(new(SizeType.AutoSize));
        appearanceActions.ColumnStyles.Add(new(SizeType.Percent, 100));
        restoreSelected.Anchor = AnchorStyles.Left; appearanceActions.Controls.Add(restoreSelected, 0, 0); appearanceActions.Controls.Add(shiny, 1, 0);
        formLabel.AutoSize = true; formLabel.Dock = DockStyle.None; formLabel.Anchor = AnchorStyles.Left; formLabel.Margin = new Padding(12, 0, 6, 0);
        formPicker.Dock = DockStyle.None; formPicker.Anchor = AnchorStyles.Left | AnchorStyles.Right; formPicker.Margin = Padding.Empty;
        formPicker.MaximumSize = new Size(240, 0); formPicker.DropDownWidth = 240; formLabel.Visible = formPicker.Visible = false;
        appearanceActions.Controls.Add(formLabel, 2, 0); appearanceActions.Controls.Add(formPicker, 3, 0);
        identity.Controls.Add(appearanceActions, 0, 3);
        hero.Controls.Add(identity, 1, 0); right.Controls.Add(hero, 0, 0);
        BuildStatsTab(); BuildMovesTab();
        var bagPage = new TabPage(T("Dinero y objetos")) { BackColor = Theme.Surface }; bagPage.Controls.Add(resources); tabs.TabPages.Add(bagPage);
        BuildDetailsTab();
        editor.Controls.Add(tabs); editor.Padding = new Padding(16, 6, 16, 0);
        right.Controls.Add(editor, 0, 1); validation.Padding = new Padding(16, 0, 12, 0); validation.AutoEllipsis = true;
        right.Controls.Add(validation, 0, 2); body.Controls.Add(right, 1, 0); root.Controls.Add(body, 0, 2);

        var footer = Table(2, 2); footer.Padding = new Padding(0, 9, 0, 0);
        // The buttons take what their language needs, and the status line keeps the rest for what the boxes have to say.
        footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.AutoSize));
        footer.RowStyles.Add(new(SizeType.Percent, 65)); footer.RowStyles.Add(new(SizeType.Percent, 35));
        footer.Controls.Add(status, 0, 0);
        var globalActions = Flow(); globalActions.FlowDirection = FlowDirection.RightToLeft; globalActions.AutoSize = true; globalActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        globalActions.Controls.Add(save); globalActions.Controls.Add(saveAs); globalActions.Controls.Add(optimizeAll);
        footer.Controls.Add(globalActions, 1, 0);
        var backupHint = new LinkLabel {
            Text = T("Abrir respaldos") + T(" · Respaldo automático al guardar · Cierra el emulador antes de aplicar cambios"),
            Dock = DockStyle.Fill, Font = Theme.Font(8), LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent,
            ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft,
        };
        backupHint.Links.Clear(); backupHint.Links.Add(0, T("Abrir respaldos").Length);
        backupHint.LinkClicked += (_, _) => {
            var folder = Path.Combine(appRoot, "Backups"); Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        };
        footer.Controls.Add(backupHint, 0, 1); footer.SetColumnSpan(backupHint, 2);
        root.Controls.Add(footer, 0, 3);
        tips.SetToolTip(previousBox, T("Caja anterior")); tips.SetToolTip(nextBox, T("Caja siguiente"));
        tips.SetToolTip(portrait, T("Ilustración y sprites descargados de PokeAPI. Si una forma no tiene imagen propia, se muestra la especie base."));
        tips.SetToolTip(formPicker, T("Forma, diseño o color de este Pokémon. Las formas que solo existen en combate, como las megaevoluciones, no aparecen."));
        tips.SetToolTip(save, T("Guarda en el archivo abierto y crea un respaldo completo de la partida anterior."));
        tips.SetToolTip(optimizeAll, T("IV de 31, naturaleza Firme o Modesta y EV según la evolución final. Revisa los cambios antes de guardar."));
    }

    // Each tool that works on the whole save adds its entry here.
    void BuildTools()
    {
        AddTool(T("Auditoría de legalidad"), () => OpenTool(new AuditWindow(document!, ShowPokemon)));
        AddTool(T("Informe de cajas"), () => OpenTool(new BoxReportWindow(document!, ShowPokemon)));
        // A save that no longer opens is the best reason to want a backup.
        AddTool(T("Administrar respaldos"), () => OpenTool(new BackupWindow(BackupRoot, document, AfterRestore)), false);
        AddTool(T("Comparar partidas"), () => OpenTool(new CompareWindow(document?.FilePath)), false);
        AddTool(T("Herramientas de Z-A…"), OpenZaTools);
        zaTool = toolsMenu.Items[^1];
        AddTool(T("Entrenador…"), () => OpenSaveTool(document!.Trainer.IsSupported, T("Esta partida no guarda un entrenador que se pueda editar aquí."), () => new TrainerDialog(document)));
        AddTool(T("Pokédex…"), () => OpenSaveTool(document!.Pokedex.CanView, T("Esta partida no tiene Pokédex."), () => new PokedexDialog(document)));
        BuildBatchTools();
        BuildBoxTools();
    }

    // Tools only prepare changes: they are saved with everything else.
    void OpenSaveTool(bool available, string unavailable, Func<Form> open)
    {
        if (!available) { Notify(unavailable); return; }
        using var dialog = open();
        dialog.ShowDialog(Window);
        ShowTrainer();
        if (document!.ToolChangeCount != 0) Notify(T("Cambios preparados. Guarda la partida para aplicarlos."));
        else UpdateState();
    }

    void ShowTrainer()
    {
        var card = document!.Trainer.Edit;
        trainer.Text = $"{card.Name}  ·  {card.Hours} h {card.Minutes} min  ·  {document.FilePath}";
    }

    void OpenZaTools()
    {
        using (var dialog = new ZaToolsDialog(document!)) dialog.ShowDialog(Window);
        resources.RefreshDraft();
        Notify(document!.Za!.HasChanges ? T("Herramientas de Z-A listas. Guarda para aplicarlas.") : status.Text);
    }

    // A tool that can't open must not take the editor, and its pending edits, down with it.
    void AddTool(string text, Action run, bool needsSave = true) => toolsMenu.Items.Add(text, null, (_, _) => {
        if (document == null && needsSave) { status.Text = T("Abre una partida primero."); return; }
        try { run(); }
        catch (Exception ex) { Warn(ex.Message); }
    });

    internal void CloseTool() => tool?.Close();

    void OpenTool(ToolWindow window)
    {
        CloseTool();
        tool = window;
        window.FormClosed += (_, _) => { if (tool == window) tool = null; };
        window.ShowBeside(Window);
    }

    void ShowPokemon(PokemonEntry entry)
    {
        if (!shown.Contains(entry)) search.Text = "";
        Pick(entry); tabs.SelectedIndex = DetailsTab;
    }

    // The open save has to follow its file, or the next save would be refused as changed outside the app.
    void AfterRestore(string path)
    {
        if (document == null || !string.Equals(Path.GetFullPath(path), document.FilePath, StringComparison.OrdinalIgnoreCase)) return;
        LoadFile(path); Notify(T("Respaldo restaurado y verificado byte a byte."));
    }

    void BuildStatsTab()
    {
        var page = new TabPage(T("Estadísticas")) { BackColor = Theme.Surface, Padding = new Padding(12, 6, 12, 4), AutoScroll = true };
        tabs.TabPages.Add(page);
        var layout = Table(1, 6);
        layout.Dock = DockStyle.Top; layout.AutoSize = true; layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new(SizeType.AutoSize));
        page.Controls.Add(layout);
        var natureRow = Table(3, 2); natureRow.AutoSize = true; natureRow.ColumnStyles.Add(new(SizeType.AutoSize));
        natureRow.ColumnStyles.Add(new(SizeType.Absolute, 240)); natureRow.ColumnStyles.Add(new(SizeType.Percent, 100));
        var natureLabel = Theme.Label(T("Naturaleza"), 10, true); natureLabel.AutoSize = true; natureLabel.Margin = new Padding(3, 10, 12, 10);
        natureRow.Controls.Add(natureLabel, 0, 0); natureRow.Controls.Add(nature, 1, 0); natureRow.Controls.Add(natureEffect, 2, 0);
        abilityLabel.AutoSize = true; abilityLabel.Margin = new Padding(3, 10, 12, 10); abilityPicker.DropDownWidth = 320;
        natureRow.Controls.Add(abilityLabel, 0, 1); natureRow.Controls.Add(abilityPicker, 1, 1); natureRow.Controls.Add(abilityNote, 2, 1);
        for (int i = 0; i < 25; i++) nature.Items.Add(new Choice(i, Catalog.Names.natures[i]));
        layout.Controls.Add(natureRow, 0, 0);
        layout.Controls.Add(equipment, 0, 1);
        var stats = Table(5, 7); stats.BackColor = Theme.Surface; stats.AutoSize = true;
        stats.ColumnStyles.Add(new(SizeType.Percent, 100)); stats.ColumnStyles.Add(new(SizeType.Absolute, 70));
        stats.ColumnStyles.Add(new(SizeType.Absolute, 122)); stats.ColumnStyles.Add(new(SizeType.Absolute, 122)); stats.ColumnStyles.Add(new(SizeType.Absolute, 100));
        Label[] headings = [Theme.Label(T("Estadística"), 9, true, Theme.Muted), Theme.Label(T("Base"), 9, true, Theme.Muted), ivHeading, evHeading, Theme.Label(T("Resultado"), 9, true, Theme.Muted)];
        for (int c = 0; c < 5; c++) {
            var label = headings[c]; label.AutoSize = true;
            label.Margin = new Padding(3, 8, 3, 8); if (c > 0) label.TextAlign = ContentAlignment.MiddleCenter;
            stats.Controls.Add(label, c, 0);
        }
        stats.RowStyles.Add(new(SizeType.AutoSize));
        for (int i = 0; i < 6; i++)
        {
            stats.RowStyles.Add(new(SizeType.AutoSize));
            var statLabel = statLabels[i] = Theme.Label(Catalog.Stats[i], 10, true); statLabel.AutoSize = true; stats.Controls.Add(statLabel, 0, i + 1);
            baseStats[i] = Theme.Label("—", 10, color: Theme.Muted); baseStats[i].TextAlign = ContentAlignment.MiddleCenter; stats.Controls.Add(baseStats[i], 1, i + 1);
            ivs[i] = Number(31); evs[i] = Number(252); stats.Controls.Add(ivs[i], 2, i + 1); stats.Controls.Add(evs[i], 3, i + 1);
            resultingStats[i] = Theme.Label("—", 11, true, Theme.Accent); resultingStats[i].TextAlign = ContentAlignment.MiddleCenter; stats.Controls.Add(resultingStats[i], 4, i + 1);
        }
        layout.Controls.Add(stats, 0, 2);
        var totals = Table(2, 1); totals.AutoSize = true; totals.MinimumSize = new Size(0, 32); totals.ColumnStyles.Add(new(SizeType.Percent, 50)); totals.ColumnStyles.Add(new(SizeType.Percent, 50));
        totals.Controls.Add(Theme.Label(T("Resultado calculado al nivel actual"), 8, color: Theme.Muted), 0, 0);
        totals.Controls.Add(evTotal, 1, 0); layout.Controls.Add(totals, 0, 3);
        var optimize = Table(4, 1); optimize.AutoSize = true; optimize.ColumnStyles.Add(new(SizeType.Absolute, 66)); optimize.ColumnStyles.Add(new(SizeType.Percent, 100));
        optimize.ColumnStyles.Add(new(SizeType.Absolute, 100)); optimize.ColumnStyles.Add(new(SizeType.Absolute, 170));
        optimize.Controls.Add(Theme.Label(T("Futuro"), 9, true), 0, 0); optimize.Controls.Add(target, 1, 0);
        maxIV.Dock = DockStyle.Fill; maxIV.Click += (_, _) => {
            if (current == null) return;
            current.Edit.IVs = Enumerable.Repeat(current.Rules.MaxIV, 6).ToArray();
            if (current.Rules.IsGameBoy) GameRules.KeepDVLooks(current.Edit, current.Original.Species);
            BindEntry(current, true); Notify(T("{0} maximizados. Guarda para aplicarlos.", current.Rules.IVName));
        };
        optimize.Controls.Add(maxIV, 2, 0);
        var auto = Theme.Button(T("Optimizar"), true); auto.Dock = DockStyle.Fill;
        auto.Click += (_, _) => {
            if (current == null) return;
            var chosen = target.SelectedItem as EvolutionTarget;
            var description = Catalog.Optimize(current, chosen); BindEntry(current, true); Notify(description);
        };
        optimize.Controls.Add(auto, 3, 0); layout.Controls.Add(optimize, 0, 4);
        buildHint.MinimumSize = new Size(0, 32); layout.Controls.Add(buildHint, 0, 5);
        tips.SetToolTip(abilityPicker, T("Principal, secundaria u oculta, según las que tenga este Pokémon en este juego. Cambia al guardar."));
        tips.SetToolTip(target, T("La optimización considera esta evolución sin evolucionar ni subir de nivel a tu Pokémon. Puedes elegir su forma actual."));
    }

    static NumericUpDown Number(int max) => new() {
        Minimum = 0, Maximum = max, Font = Theme.Font(10), Anchor = AnchorStyles.Left | AnchorStyles.Right,
        TextAlign = HorizontalAlignment.Center, Margin = new Padding(10, 5, 10, 5), BorderStyle = BorderStyle.FixedSingle,
    };

    void BuildMovesTab()
    {
        var page = new TabPage(T("Movimientos")) { BackColor = Theme.Surface, Padding = new Padding(18, 12, 18, 8), AutoScroll = true };
        tabs.TabPages.Add(page);
        var layout = Table(1, 3); layout.Dock = DockStyle.Top; layout.AutoSize = true; layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        for (int i = 0; i < 3; i++) layout.RowStyles.Add(new(SizeType.AutoSize)); page.Controls.Add(layout);
        moveHint.MinimumSize = new Size(0, 56);
        layout.Controls.Add(moveHint, 0, 0);
        var slots = Table(2, 4); slots.AutoSize = true; slots.ColumnStyles.Add(new(SizeType.AutoSize)); slots.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) {
            slots.RowStyles.Add(new(SizeType.AutoSize));
            var label = Theme.Label(T("Movimiento {0}", i + 1), 10, true); label.AutoSize = true; label.Margin = new Padding(3, 7, 14, 7);
            slots.Controls.Add(label, 0, i);
            moves[i] = new MovePicker(); slots.Controls.Add(moves[i], 1, i);
        }
        layout.Controls.Add(slots, 0, 1);
        var help = Theme.Label(T("Escribe parte del nombre para buscar. Enter confirma y Esc cancela.\nLa lista incluye los movimientos de este juego para el Pokémon, junto con los que ya conoce."), 9, color: Theme.Muted);
        help.MinimumSize = new Size(0, 60); help.Margin = new Padding(3, 12, 3, 3); layout.Controls.Add(help, 0, 2);
    }

    void BuildDetailsTab()
    {
        var page = new TabPage(T("Detalles")) { BackColor = Theme.Surface, Padding = new Padding(12, 8, 12, 6) };
        tabs.TabPages.Add(page);
        var layout = Table(2, 2);
        layout.ColumnStyles.Add(new(SizeType.Absolute, 380)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize));
        page.Controls.Add(layout);
        var fields = Table(2, 5); fields.ColumnStyles.Add(new(SizeType.AutoSize)); fields.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) fields.RowStyles.Add(new(SizeType.AutoSize));
        fields.RowStyles.Add(new(SizeType.Percent, 100));
        fields.Controls.Add(FieldLabel(T("Apodo")), 0, 0); fields.Controls.Add(nicknameBox, 1, 0);
        fields.Controls.Add(FieldLabel(T("Entrenador original")), 0, 1); fields.Controls.Add(trainerNameBox, 1, 1);
        fields.Controls.Add(FieldLabel(T("Nivel")), 0, 2); fields.Controls.Add(levelBox, 1, 2);
        levelBox.Minimum = 1; levelBox.Margin = new Padding(4, 6, 4, 4);
        fields.Controls.Add(ballLabel, 0, 3); fields.Controls.Add(ballPicker, 1, 3);
        layout.Controls.Add(fields, 0, 0);
        var legality = Table(1, 3); legality.Margin = new Padding(16, 0, 0, 0);
        legality.RowStyles.Add(new(SizeType.AutoSize)); legality.RowStyles.Add(new(SizeType.Absolute, 34)); legality.RowStyles.Add(new(SizeType.Percent, 100));
        var legalityTitle = Theme.Label(T("Legalidad"), 9, true, Theme.Muted); legalityTitle.AutoSize = true; legalityTitle.Margin = new Padding(3, 8, 3, 2);
        legality.Controls.Add(legalityTitle, 0, 0); legality.Controls.Add(legalityVerdict, 0, 1); legality.Controls.Add(legalityReport, 0, 2);
        layout.Controls.Add(legality, 1, 0);
        var showdown = Flow(); showdown.AutoSize = true; showdown.Dock = DockStyle.Top;
        var copySet = Theme.Button(T("Copiar Showdown")); var pasteSet = Theme.Button(T("Pegar Showdown"));
        copySet.Click += (_, _) => {
            if (current == null) return;
            Clipboard.SetText(Catalog.ShowdownText(current)); Notify(T("Set copiado al portapapeles."));
        };
        pasteSet.Click += (_, _) => { if (current != null && Clipboard.ContainsText()) PasteShowdown(Clipboard.GetText()); };
        showdown.Controls.Add(copySet); showdown.Controls.Add(pasteSet);
        layout.Controls.Add(showdown, 0, 1); layout.SetColumnSpan(showdown, 2);
        tips.SetToolTip(nicknameBox, T("Déjalo vacío para volver al nombre de la especie."));
        tips.SetToolTip(pasteSet, T("Aplica a este Pokémon el set copiado. Lo que no pueda usar se indica abajo."));
    }

    void PasteShowdown(string text)
    {
        try
        {
            var message = document!.ImportShowdown(current!, text);
            BindEntry(current!, true); resources.RefreshDraft(); Notify(message);
        }
        catch (InvalidOperationException ex) { Notify(ex.Message); }
    }

    void ShowLegality()
    {
        if (current == null || tabs.SelectedIndex != DetailsTab) return;
        var result = Catalog.CheckLegality(current);
        legalityVerdict.Text = result.Valid ? T("✔ Legal") : T("✖ No legal");
        legalityVerdict.ForeColor = result.Valid ? Theme.Accent : Theme.Danger;
        legalityReport.Text = result.Report.ReplaceLineEndings("\r\n");
    }

    void WireEvents()
    {
        resources.DraftChanged += (_, _) => UpdateState(); resources.StatusMessage += Notify;
        equipment.DraftChanged += (_, _) => UpdateState(); equipment.StatusMessage += Notify;
        equipment.BagChanged += (_, _) => resources.RefreshDraft();
        equipment.BagRequested += id => { resources.ShowItem(id); tabs.SelectedIndex = BagTab; };
        tabs.SelectedIndexChanged += (_, _) => {
            bool showBag = tabs.SelectedIndex == BagTab;
            if (detailsLayout != null && heroPanel != null) {
                detailsLayout.SuspendLayout(); heroPanel.Visible = !showBag;
                detailsLayout.RowStyles[0].Height = showBag ? 0 : HeroHeight; detailsLayout.ResumeLayout();
            }
            optimizeAll.Visible = !showBag;
            if (tabs.SelectedIndex == 0 && document != null && current != null) equipment.Bind(document, current, true);
            ShowLegality();
        };
        search.TextChanged += (_, _) => RefreshList();
        teamGrid.Picked += Pick; boxGrid.Picked += Pick;
        boxPicker.SelectedIndexChanged += (_, _) => { if (boxPicker.SelectedIndex >= 0 && boxPicker.SelectedIndex != box) ShowBox(boxPicker.SelectedIndex); };
        previousBox.Click += (_, _) => ShowBox(box - 1); nextBox.Click += (_, _) => ShowBox(box + 1);
        nature.SelectedIndexChanged += (_, _) => {
            if (binding || current == null || nature.SelectedItem is not Choice choice) return;
            current.Edit.Nature = (Nature)choice.Id; UpdateState();
        };
        abilityPicker.SelectedIndexChanged += (_, _) => {
            if (binding || current == null || abilityPicker.SelectedItem is not AbilityChoice choice) return;
            Catalog.SetAbility(current, choice); UpdateState();
        };
        nicknameBox.TextChanged += (_, _) => { if (!binding && current != null) { current.Edit.Nickname = nicknameBox.Text; UpdateState(); } };
        trainerNameBox.TextChanged += (_, _) => { if (!binding && current != null) { current.Edit.Trainer = trainerNameBox.Text; UpdateState(); } };
        levelBox.ValueChanged += (_, _) => { if (!binding && current != null) { current.Edit.Level = (int)levelBox.Value; UpdateState(); } };
        ballPicker.SelectedIndexChanged += (_, _) => {
            if (!binding && current != null && ballPicker.SelectedItem is Choice ball) { current.Edit.Ball = ball.Id; UpdateState(); }
        };
        target.SelectedIndexChanged += (_, _) => UpdateBuildHint();
        formPicker.SelectedIndexChanged += (_, _) => {
            if (binding || current == null || document == null || formPicker.SelectedItem is not FormChoice choice) return;
            var message = document.SetForm(current, choice);
            BindEntry(current, true); resources.RefreshDraft(); Notify(message);
        };
        shiny.CheckedChanged += (_, _) => {
            if (binding || current == null) return;
            Catalog.SetShiny(current, shiny.Checked);
            if (current.Rules.IsGameBoy) BindEntry(current, true); else UpdateState();
        };
        for (int i = 0; i < 6; i++) {
            int j = i;
            ivs[i].ValueChanged += (_, _) => { if (!binding && current != null) { current.Edit.IVs[j] = (int)ivs[j].Value; UpdateState(); } };
            evs[i].ValueChanged += (_, _) => { if (!binding && current != null) { current.Edit.EVs[j] = (int)evs[j].Value; UpdateState(); } };
        }
        for (int i = 0; i < 4; i++) {
            int j = i;
            moves[i].MoveCommitted += (_, _) => {
                if (!binding && current != null && moves[j].SelectedChoice is Choice choice) {
                    current.Edit.Moves[j] = (ushort)choice.Id; UpdateState();
                }
            };
        }
        restoreSelected.Click += (_, _) => {
            if (current == null || document == null) return;
            try { document.ResetPokemon(current); BindEntry(current, true); resources.RefreshDraft(); Notify(T("Cambios descartados para este Pokémon.")); }
            catch (InvalidOperationException ex) { Notify(ex.Message); }
        };
        optimizeAll.Click += (_, _) => {
            if (document == null) return;
            foreach (var p in document.Pokemon) Catalog.Optimize(p);
            if (current != null) BindEntry(current, true);
            Notify(T("{0} Pokémon preparados según su evolución final. Revisa los cambios y guarda.", document.Pokemon.Count));
        };
        save.Click += (_, _) => SaveFile(false); saveAs.Click += (_, _) => SaveFile(true);
    }

    void UpdateBuildHint()
    {
        if (target.SelectedItem is not EvolutionTarget t) return;
        if (document == null) return;
        var pi = document.Rules.Personal[t.Species, t.Form];
        buildHint.Text = T("{0}: base de Ataque {1} · At. Especial {2} · Velocidad {3}", t.Label, pi.ATK, pi.SPA, pi.SPE);
    }

    void BindEntry(PokemonEntry e, bool keepTarget = false)
    {
        binding = true;
        var previousTarget = keepTarget ? target.SelectedItem as EvolutionTarget : null;
        current = e;
        Catalog.SyncForm(e);
        heroName.Text = e.DisplayName + (e.Edit.IsShiny ? " ✦" : "");
        bool hatched = !e.Original.IsEgg;
        nicknameBox.MaxLength = e.Original.MaxStringLengthNickname; trainerNameBox.MaxLength = e.Original.MaxStringLengthTrainer;
        nicknameBox.Text = e.Edit.Nickname; trainerNameBox.Text = e.Edit.Trainer; levelBox.Value = Math.Clamp(e.Edit.Level, 1, Experience.MaxLevel);
        var balls = Catalog.GetBalls(e);
        ballPicker.Items.Clear(); ballPicker.Items.AddRange(balls.Cast<object>().ToArray());
        ballPicker.SelectedItem = balls.FirstOrDefault(b => b.Id == e.Edit.Ball);
        nicknameBox.Enabled = trainerNameBox.Enabled = levelBox.Enabled = ballPicker.Enabled = hatched;
        var pi = e.Preview().PersonalInfo;
        var forms = Catalog.GetForms(e);
        formPicker.Items.Clear(); formPicker.Items.AddRange(forms.Cast<object>().ToArray());
        formLabel.Visible = formPicker.Visible = forms.Count != 0;
        ShowForm(e);
        portrait.Image = sprites.Get(e.Original.Species, e.ImageVariant, true, e.Edit.IsShiny);
        shiny.Checked = e.Edit.IsShiny;
        int[] bases = [pi.HP, pi.ATK, pi.DEF, pi.SPE, pi.SPA, pi.SPD];
        for (int i = 0; i < 6; i++) {
            ivs[i].Value = Math.Clamp(e.Edit.IVs[i], 0, (int)ivs[i].Maximum); evs[i].Value = Math.Clamp(e.Edit.EVs[i], 0, (int)evs[i].Maximum);
            baseStats[i].Text = bases[i].ToString();
        }
        nature.SelectedIndex = (int)e.Edit.Nature;
        ShowAbilities(e);
        var available = Catalog.GetMoves(e);
        for (int i = 0; i < 4; i++) {
            moves[i].SetChoices(available, e.Edit.Moves[i]);
        }
        moveHint.Text = T("{0} movimientos compatibles para {1}.\nEscribe el nombre o una parte en cualquier casilla para buscar.", available.Count - 1, e.SpeciesName);
        target.Items.Clear(); var targets = Catalog.GetFinalForms(e);
        target.Items.AddRange(targets.Cast<object>().ToArray());
        if (!targets.Any(t => t.Species == e.Original.Species && t.Form == e.Edit.Form))
            target.Items.Add(new EvolutionTarget(e.Original.Species, e.Edit.Form, T("{0} (actual)", e.SpeciesName)));
        target.SelectedIndex = 0;
        if (previousTarget != null && target.Items.Cast<EvolutionTarget>().FirstOrDefault(t => t.Species == previousTarget.Species && t.Form == previousTarget.Form) is { } old)
            target.SelectedItem = old;
        if (document != null) equipment.Bind(document, e);
        binding = false; UpdateBuildHint(); UpdateState();
    }

    void ShowAbilities(PokemonEntry e)
    {
        var abilities = Catalog.GetAbilities(e);
        abilityPicker.Items.Clear(); abilityPicker.Items.AddRange(abilities.Cast<object>().ToArray());
        abilityPicker.SelectedItem = abilities.FirstOrDefault(c => c.Ability == e.Edit.Ability);
        abilityPicker.Enabled = abilities.Count > 1;
        abilityNote.Text = !e.Rules.UsesAbility ? T("Este juego no usa habilidades.")
            : abilities.Count == 1 ? T("Este Pokémon solo tiene esta habilidad.") : "";
    }

    // Types depend on the form, and on Game Boy games the letter can follow an edited DV.
    void ShowForm(PokemonEntry e)
    {
        bool wasBinding = binding; binding = true;
        formPicker.SelectedItem = formPicker.Items.Cast<FormChoice>().FirstOrDefault(c => c.Form == e.Edit.Form && c.Argument == e.Edit.FormArgument);
        binding = wasBinding;
        var pi = e.Preview().PersonalInfo;
        var name = (formPicker.SelectedItem as FormChoice)?.Label ?? (e.Edit.Form == 0 ? "" : Catalog.FormName(e.Rules, e.Original.Species, e.Edit.Form));
        heroTypes.Text = Catalog.TypeName(pi.Type1) + (pi.Type2 != pi.Type1 ? "  /  " + Catalog.TypeName(pi.Type2) : "") + (name.Length == 0 ? "" : "  ·  " + name);
    }

    void UpdateState()
    {
        bool loaded = document != null;
        if (zaTool != null) zaTool.Available = document?.Rules.IsZA == true;
        toolsButton.Visible = toolsMenu.Items.Cast<ToolStripItem>().Any(item => item.Available);
        shiny.Enabled = loaded && current != null;
        editor.Enabled = loaded; tabs.TabPages[0].Enabled = tabs.TabPages[1].Enabled = tabs.TabPages[DetailsTab].Enabled = loaded && current != null;
        equipment.Enabled = loaded && current != null;
        restoreSelected.Enabled = current?.Dirty == true;
        optimizeAll.Enabled = loaded && document!.Pokemon.Count != 0;
        string? error = document?.ValidateEdits();
        validation.Text = error ?? "";
        save.Enabled = loaded && document!.HasChanges && error == null;
        save.BackColor = save.Enabled ? Theme.Accent : Theme.Line;
        saveAs.Enabled = loaded && error == null;
        if (current != null)
        {
            Catalog.SyncForm(current); ShowForm(current);
            heroName.Text = current.DisplayName + (current.Edit.IsShiny ? " ✦" : "");
            heroSpecies.Text = T("{0}  ·  Nivel {1}  ·  {2}", current.SpeciesName, current.Edit.Level, current.Location);
            portrait.Image = sprites.Get(current.Original.Species, current.ImageVariant, true, current.Edit.IsShiny);
            natureEffect.Text = current.Rules.HasNature ? Catalog.NatureEffect(current.Edit.Nature) : T("Este juego no tiene naturalezas.");
            var preview = current.Preview();
            var stats = preview.GetStats(preview.PersonalInfo);
            for (int i = 0; i < 6; i++) resultingStats[i].Text = stats[i].ToString();
            int total = current.Edit.EVs.Sum(), limit = current.Rules.MaxEVTotal;
            evTotal.Text = limit == 0 ? T("{0}: sin límite total", current.Rules.EVName) : T("EV: {0} / {1}  ·  Quedan {2}", total, limit, Math.Max(0, limit - total));
            evTotal.ForeColor = limit != 0 && total > limit ? Theme.Danger : Theme.Accent;
        }
        if (error == null) ShowLegality();
        Text = AppTitle + (loaded && document!.HasChanges ? T(" · {0} cambios pendientes", document.DirtyCount + document.ResourceChangeCount + (document.Za?.ChangeCount ?? 0) + document.ToolChangeCount + document.BoxChangeCount) : "");
        teamGrid.Invalidate(); boxGrid.Invalidate();
    }

    void RefreshList()
    {
        if (document == null) return;
        var query = search.Text.Trim();
        shown = document.Pokemon.Where(p => query.Length == 0 || System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(p.ToString(), query,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0).ToList();
        if (current != null && shown.Contains(current)) ShowSlots();
        else if (shown.Count > 0) Pick(shown[0]);
        else {
            current = null; portrait.Image = null; heroName.Text = T("Sin resultados"); formLabel.Visible = formPicker.Visible = false;
            heroSpecies.Text = T("Prueba con otro nombre."); heroTypes.Text = "";
            ShowSlots();
        }
        count.Text = T("TUS POKÉMON · {0} / {1}", shown.Count, document.Pokemon.Count);
        UpdateState();
    }

    // While searching, whoever matches in any box takes the place of the open one.
    void ShowSlots()
    {
        if (document == null) return;
        bool searching = search.Text.Trim().Length != 0;
        teamGrid.Movable = boxGrid.Movable = !searching && document.CanArrange;
        teamGrid.SetSlots(document.TeamSlots(), SaveDocument.TeamSize, searching ? shown : null);
        if (searching) boxGrid.SetSlots(shown.Where(p => !p.IsParty), SaveDocument.TeamSize);
        else boxGrid.SetSlots(document.BoxSlots(box), document.Rules.BoxColumns);
        teamGrid.Highlight(current); boxGrid.Highlight(current);
        boxPicker.Enabled = previousBox.Enabled = nextBox.Enabled = !searching && document.BoxCount > 1;
        if (box < boxPicker.Items.Count) boxPicker.SelectedIndex = box;
    }

    void ShowBox(int index)
    {
        if (document == null || document.BoxCount == 0) return;
        box = (index + document.BoxCount) % document.BoxCount;
        ShowSlots();
    }

    void Pick(PokemonEntry entry)
    {
        if (!entry.IsParty) box = document!.BoxOf(entry);
        BindEntry(entry); ShowSlots();
    }

    public void LoadFile(string path)
    {
        try
        {
            UseWaitCursor = true;
            Attach(SaveDocument.Open(path));
            Notify(T("{0} Pokémon cargados. Tu partida no cambia hasta que guardes.", document!.Pokemon.Count));
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { UseWaitCursor = false; }
    }

    void Attach(SaveDocument loaded)
    {
        CloseTool();
        current = null; document = loaded; ApplyRules(loaded.Rules); resources.Bind(document);
        box = document.OpeningBox; boxPicker.Items.Clear(); boxPicker.Items.AddRange(Enumerable.Range(0, document.BoxCount).Select(document.BoxLabel).ToArray<object>());
        search.Text = "";
        ShowTrainer();
        tips.SetToolTip(trainer, document.FilePath);
        RefreshList(); UpdateState();
    }

    internal Snapshot TakeSnapshot() => new(document, current, box, search.Text, tabs.SelectedIndex);

    internal void Resume(Snapshot state)
    {
        if (state.Document != null)
        {
            Attach(state.Document);
            search.Text = state.Search;
            if (state.Entry != null && shown.Contains(state.Entry)) Pick(state.Entry);
            ShowBox(state.Box);
        }
        tabs.SelectedIndex = state.Tab;
        settingsButton.Select();
        Notify(T("Configuración aplicada."));
    }

    // Each generation has its own ranges and leaves out what did not exist yet.
    void ApplyRules(GameRules rules)
    {
        binding = true;
        brand.Text = BrandName;
        ivHeading.Text = $"{rules.IVName} · 0–{rules.MaxIV}"; evHeading.Text = $"{rules.EVName} · 0–{rules.MaxEV}";
        maxIV.Text = T("{0} a {1}", rules.IVName, rules.MaxIV);
        for (int i = 0; i < 6; i++) {
            ivs[i].Maximum = rules.MaxIV; evs[i].Maximum = rules.MaxEV; statLabels[i].Text = rules.StatName(i);
            ivs[i].Enabled = rules.IsStatEditable(i, true); evs[i].Enabled = rules.IsStatEditable(i, false);
        }
        nature.Enabled = rules.HasNature; shiny.Visible = rules.HasShiny; equipment.Visible = rules.HasHeldItems;
        abilityLabel.Visible = abilityPicker.Visible = abilityNote.Visible = rules.HasAbility;
        ballLabel.Visible = ballPicker.Visible = rules.HasBall;
        bool hasTeam = rules.Save.HasParty;
        teamTitle.Visible = teamGrid.Visible = hasTeam;
        sidebar.RowStyles[TeamTitleRow].Height = hasTeam ? TeamTitleHeight : 0;
        sidebar.RowStyles[TeamGridRow].Height = hasTeam ? TeamGridHeight : 0;
        binding = false;
    }

    async Task FindSaves()
    {
        if (!CanDiscard()) return;
        List<FoundSave> found;
        try { UseWaitCursor = true; Notify(T("Buscando partidas…")); found = await Task.Run(() => SaveLocator.FindAll(appRoot)); }
        finally { UseWaitCursor = false; }
        if (found.Count == 0) { ShowError(T("No encontré partidas. Usa «Abrir partida» y selecciona el archivo de guardado.")); return; }
        var chosen = found.Count == 1 ? found[0] : SavePicker.Pick(Window, found);
        if (chosen != null) LoadFile(chosen.Path);
        else Notify(T("Búsqueda cancelada."));
    }

    internal void OpenResources() => tabs.SelectedIndex = BagTab;
    internal void OpenEquipment() => tabs.SelectedIndex = 0;

    void OpenDialog()
    {
        if (!CanDiscard()) return;
        using var dialog = new OpenFileDialog { Title = T("Abrir partida de Pokémon"), Filter = T("Partidas de Pokémon") + "|main;savedata.bin;*.sav;*.srm;*.dsv;*.sa1;*.sa2;*.sa3;*.gci;*.bin;*.dat;*.raw|" + T("Todos los archivos") + "|*.*" };
        if (document != null) dialog.InitialDirectory = Path.GetDirectoryName(document.FilePath);
        if (dialog.ShowDialog(Window) == DialogResult.OK) LoadFile(dialog.FileName);
    }

    void SaveFile(bool asCopy)
    {
        if (document == null) return;
        var destination = document.FilePath;
        if (asCopy)
        {
            using var dialog = new SaveFileDialog { Title = T("Guardar una copia de la partida"), Filter = T("Archivo de partida") + "|*.*", FileName = Path.GetFileName(document.FilePath), AddExtension = false, OverwritePrompt = true };
            dialog.InitialDirectory = Path.GetDirectoryName(document.FilePath);
            if (dialog.ShowDialog(Window) != DialogResult.OK) return;
            destination = dialog.FileName;
        }
        try
        {
            UseWaitCursor = true; var selectedSpecies = current?.Index; bool? selectedParty = current?.IsParty; int openBox = box;
            var backup = document.Write(destination, Path.Combine(appRoot, "Backups"));
            LoadFile(destination);
            if (selectedSpecies != null && document!.Pokemon.FirstOrDefault(e => e.Index == selectedSpecies && e.IsParty == selectedParty) is { } selected)
                Pick(selected);
            ShowBox(openBox);
            Notify(T("Partida guardada y verificada. Respaldo automático creado.")); tips.SetToolTip(status, backup);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { UseWaitCursor = false; }
    }

    internal bool CanDiscard() => document?.HasChanges != true || MessageBox.Show(Window,
        T("Tienes cambios sin guardar. ¿Quieres descartarlos?"), T("Cambios pendientes"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
        MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    void Notify(string text) { status.Text = text; status.ForeColor = Theme.Muted; UpdateState(); }
    void ShowError(string text) { status.Text = text; status.ForeColor = Theme.Danger; MessageBox.Show(Window, text, "Editrainer", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    internal bool HandleShortcut(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.O)) { OpenDialog(); return true; }
        if (keyData == (Keys.Control | Keys.S)) { if (save.Enabled) SaveFile(false); return true; }
        return false;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { tips.Dispose(); slotMenu.Dispose(); }
        base.Dispose(disposing);
    }

    public void CaptureScreenshot(string path, int index, int page, bool shinyPreview = false)
    {
        if (document != null && index >= 0 && index < document.Pokemon.Count) Pick(document.Pokemon[index]);
        if (shinyPreview) shiny.Checked = true;
        tabs.SelectedIndex = Math.Clamp(page, 0, tabs.TabCount - 1); Refresh();
        using var bitmap = Theme.Capture(Window);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    // Dialogs block the run that opens them, so the checks look at them from far off screen.
    static T OffScreen<T>(T dialog) where T : Form
    {
        dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-16000, -16000);
        dialog.Show(); Application.DoEvents();
        return dialog;
    }

    /// <summary>For the screenshots, which can't wait on a dialog.</summary>
    public void CaptureTool(string path, string tool)
    {
        using Form dialog = OffScreen<Form>(tool == "trainer" ? new TrainerDialog(document!) : new PokedexDialog(document!));
        dialog.Refresh();
        using var bitmap = Theme.Capture(dialog);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    void CheckTools(Action<bool, string> Check, string sizeLabel)
    {
        foreach (var dialog in new Form[] { new TrainerDialog(document!), new PokedexDialog(document!) })
        {
            using (OffScreen(dialog))
            {
                var clipped = Clipped(dialog);
                Check(clipped.Length == 0, $"Every text fits in {dialog.Text} ({sizeLabel}) {clipped}");
                Check(dialog.Width <= Window.MinimumSize.Width && dialog.Height <= Window.MinimumSize.Height, $"{dialog.Text} fits a small screen ({dialog.Size})");
                var cut = Descendants(dialog).FirstOrDefault(c => c.Visible && dialog.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)).Right > dialog.ClientSize.Width);
                Check(cut == null, $"Nothing is cut off on the right of {dialog.Text} ({cut?.GetType().Name} {cut?.Text})");
            }
        }
    }

    public void RunInteractionChecks(string reportPath)
    {
        var checks = new List<string>();
        void Check(bool ok, string description) { if (!ok) throw new Exception(description); checks.Add(description); }
        void Choose(int index) => Pick(document!.Pokemon[index]);
        Check(document != null && shown.Count == document.Pokemon.Count, "Lists all loaded Pokémon");
        Check(teamGrid.Slots.Count == SaveDocument.TeamSize && teamGrid.Slots.Count(s => s != null) == document!.Save.PartyCount, "The team takes its six places above the box");
        Check(boxGrid.Slots.Count == document!.Save.BoxSlotCount && boxGrid.Slots.SequenceEqual(document.BoxSlots(box)), "The box grid shows every place of the open box, empty ones included");
        Choose(4);
        Check(current?.Original.Species == 179 && portrait.Image != null, "Selection shows Mareep's artwork");
        var originalIV = ivs[0].Value; ivs[0].Value = originalIV == 13 ? 14 : 13;
        Check(document!.DirtyCount == 1 && save.Enabled, "Editing an IV stages a change and enables Save");
        Choose(2); Choose(4);
        Check(ivs[0].Value != originalIV, "Switching selections preserves edits");
        evs[1].Value = 252;
        Check(!save.Enabled && validation.Text.Contains("máximo"), "Invalid EV total disables Save and explains the error");
        restoreSelected.PerformClick(); Check(document.DirtyCount == 0 && !save.Enabled, "Undo resets only the selected Pokémon");
        nature.SelectedIndex = (int)Nature.Adamant;
        Check(natureEffect.Text.Contains("↑ Ataque") && current!.Edit.Nature == Nature.Adamant, "Nature selection updates effect and draft");
        restoreSelected.PerformClick();
        var boxed = document.Pokemon.First(p => !p.IsParty);
        ShowBox(document.BoxOf(boxed));
        boxGrid.PressSlot(boxed.Index % document.Save.BoxSlotCount);
        Check(current == boxed, "Clicking a box place selects its Pokémon");
        boxGrid.PressSlot(boxGrid.Slots.ToList().FindIndex(s => s == null));
        Check(current == boxed, "Clicking an empty place keeps the selection");
        teamGrid.PressSlot(4);
        Check(current == document.Pokemon[4], "Clicking a team place selects its Pokémon");
        teamGrid.PressKey(Keys.Left);
        Check(current == document.Pokemon[3], "Arrow keys move between places");
        int opened = box; nextBox.PerformClick();
        Check(box == (opened + 1) % document.BoxCount && boxPicker.SelectedIndex == box && current == document.Pokemon[3], "Next box turns the page and keeps the selection");
        ShowBox(0); previousBox.PerformClick();
        Check(box == document.BoxCount - 1, "Box arrows wrap around");
        boxPicker.SelectedIndex = 2;
        Check(box == 2 && boxGrid.Slots.SequenceEqual(document.BoxSlots(2)), "The box list jumps to a box");
        Pick(boxed);
        Check(box == document.BoxOf(boxed) && boxGrid.Slots.Contains(boxed), "Selecting a boxed Pokémon opens its box");
        search.Text = boxed.SpeciesName;
        Check(shown.Count == 1 && boxGrid.Slots.SequenceEqual([boxed]) && !boxPicker.Enabled && !nextBox.Enabled, "Search shows its matches in place of the box");
        search.Text = "bunnelby";
        Check(shown.Count == 1 && current!.Original.Species == 659 && boxGrid.Slots.Count == 0, "Search selects the matching Pokémon");
        search.Text = "";
        Check(boxPicker.Enabled && boxGrid.Slots.Count == document.Save.BoxSlotCount, "Clearing the search brings the box back");
        Choose(2);
        tabs.SelectedIndex = 1; Check(moves[2].SelectedItem is Choice m && m.Id == (int)PKMove.RockSmash, "Moves tab binds the equipped moves");
        search.Text = "no-existe-ningun-pokemon";
        Check(shown.Count == 0 && !tabs.TabPages[0].Enabled && portrait.Image == null && tabs.TabPages[2].Enabled,
            "Empty Pokémon search disables Pokémon fields while keeping resources accessible");
        search.Text = "";
        Choose(1); tabs.SelectedIndex = 1;
        ushort originalThirdMove = current!.Edit.Moves[2];
        moves[2].Search("aereo");
        Check(moves[2].Items.Count == 1 && moves[2].Items[0] is Choice aerial && aerial.Id == (int)PKMove.AerialAce,
            "Typing a move fragment filters by name without requiring accents");
        Check(current.Edit.Moves[2] == originalThirdMove, "Searching does not equip a move until confirmed");
        moves[2].ConfirmSearch();
        Check(current.Edit.Moves[2] == (ushort)PKMove.AerialAce && moves[2].Text == Catalog.Names.movelist[(int)PKMove.AerialAce],
            "Enter equips the filtered move and displays its complete name");
        moves[2].Search("movimiento-que-no-existe");
        Check(moves[2].Items.Count == 0, "Unknown move search has no matching choices");
        moves[2].CancelSearch();
        Check(moves[2].SelectedChoice?.Id == (int)PKMove.AerialAce && current.Edit.Moves[2] == (ushort)PKMove.AerialAce,
            "Escape restores the equipped move without changing the draft");
        var normalPortrait = portrait.Image; shiny.Checked = true;
        Check(current.Edit.IsShiny && current.Preview().IsShiny && heroName.Text.Contains("✦"), "Shiny checkbox updates preview and the name marker");
        Check(portrait.Image != null && !ReferenceEquals(normalPortrait, portrait.Image), "Shiny checkbox displays the downloaded shiny artwork");
        Choose(4); Choose(1);
        Check(shiny.Checked && current!.Edit.IsShiny, "Shiny selection persists when switching Pokémon");
        restoreSelected.PerformClick();
        Check(!shiny.Checked && !current!.Edit.IsShiny && document.DirtyCount == 0, "Undo restores the original shiny state and moves");
        tabs.SelectedIndex = 0;
        var changeable = document.Pokemon.First(p => Catalog.GetForms(p).Count > 1);
        var plain = document.Pokemon.First(p => Catalog.GetForms(p).Count == 0);
        Pick(plain);
        Check(!formPicker.Visible && !formLabel.Visible, "Pokémon with a single form hide the form selector");
        Pick(changeable);
        Check(formPicker.Visible && formPicker.Items.Count > 1 && formPicker.SelectedItem is FormChoice, "Pokémon with forms show the selector on their current form");
        byte startingForm = current!.Edit.Form;
        formPicker.SelectedIndex = formPicker.SelectedIndex == 0 ? 1 : 0;
        Check(current.Edit.Form != startingForm && current.Preview().Form == current.Edit.Form && current.Dirty && save.Enabled, "Choosing a form stages it and enables Save");
        Check(heroTypes.Text.Contains(((FormChoice)formPicker.SelectedItem!).Label), "The header names the chosen form");
        restoreSelected.PerformClick();
        Check(current.Edit.Form == startingForm && ((FormChoice)formPicker.SelectedItem!).Form == startingForm && document.DirtyCount == 0, "Undo restores the original form");
        tabs.SelectedIndex = 0;
        var gifted = document.Pokemon.First(p => Catalog.GetAbilities(p).Count > 1);
        Pick(gifted);
        Check(abilityPicker.Visible && abilityPicker.Enabled && abilityPicker.SelectedItem is AbilityChoice, "Pokémon with several abilities show the selector on their current one");
        var startingAbility = (AbilityChoice)abilityPicker.SelectedItem!;
        abilityPicker.SelectedIndex = abilityPicker.SelectedIndex == 0 ? 1 : 0;
        var chosenAbility = (AbilityChoice)abilityPicker.SelectedItem!;
        Check(current!.Dirty && save.Enabled && current.Preview().Ability == chosenAbility.Ability, "Choosing an ability stages it and enables Save");
        Check(((AbilityChoice)abilityPicker.Items[abilityPicker.Items.Count - 1]!).Label.Contains("oculta"), "The selector offers the hidden ability");
        Choose(document.Pokemon.IndexOf(gifted) == 0 ? 1 : 0); Pick(gifted);
        Check(((AbilityChoice)abilityPicker.SelectedItem!).Ability == chosenAbility.Ability, "The chosen ability persists when switching Pokémon");
        restoreSelected.PerformClick();
        Check(((AbilityChoice)abilityPicker.SelectedItem!).Ability == startingAbility.Ability && document.DirtyCount == 0, "Undo restores the original ability");
        Choose(4); tabs.SelectedIndex = DetailsTab;
        int startingLevel = current!.Edit.Level;
        levelBox.Value = startingLevel == 50 ? 51 : 50;
        Check(current.Preview().CurrentLevel == (int)levelBox.Value && heroSpecies.Text.Contains(levelBox.Value.ToString()) && save.Enabled, "Changing the level stages it and updates the header");
        nicknameBox.Text = "Lanita";
        Check(current.Preview().Nickname == "Lanita" && heroName.Text == "Lanita", "Changing the nickname stages it and updates the header");
        trainerNameBox.Text = "";
        Check(!save.Enabled && validation.Text.Length != 0, "An empty trainer name disables Save and explains why");
        trainerNameBox.Text = "Ana";
        Check(current.Preview().OriginalTrainerName == "Ana" && save.Enabled, "Changing the trainer name stages it");
        var otherBall = ballPicker.Items.Cast<Choice>().First(b => b.Id != current.Edit.Ball);
        ballPicker.SelectedItem = otherBall;
        Check(current.Preview().Ball == otherBall.Id, "Choosing a Poké Ball stages it");
        Check(legalityVerdict.Text.Length != 0 && legalityReport.Text.Length != 0, "The details tab shows a legality report");
        restoreSelected.PerformClick();
        Check(document.DirtyCount == 0 && (int)levelBox.Value == startingLevel && nicknameBox.Text == current.Edit.Nickname, "Undo restores the details");
        string copied = Catalog.ShowdownText(current);
        PasteShowdown(copied);
        Check(document.DirtyCount == 0, "Pasting a Pokémon's own set changes nothing");
        PasteShowdown("Pikachu\nLevel: 5");
        Check(status.Text.Contains("Pikachu") && document.DirtyCount == 0, "A set for another species is refused");
        RunBoxChecks(Check, Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        Choose(4); tabs.SelectedIndex = BagTab; resources.RunInteractionChecks(Check);
        Check(!save.Enabled && !document.HasChanges && heroPanel?.Visible == false, "Resource undo clears global pending changes and the bag tab hides the Pokémon header");
        Choose(4); tabs.SelectedIndex = 0; equipment.RunInteractionChecks(Check); resources.RefreshDraft(); UpdateState();
        Check(!document.HasChanges && !save.Enabled, "Equipment and friendship test edits can be completely undone");
        CheckZaTools(Check);
        CheckSaveTools(Check, ToolScratch(reportPath));
        Check(toolsMenu.Items.Cast<ToolStripItem>().Any(i => i.Text == T("Entrenador…")) && toolsMenu.Items.Cast<ToolStripItem>().Any(i => i.Text == T("Pokédex…")), "The Tools menu offers the trainer and the Pokédex");
        using (var dialog = OffScreen(new TrainerDialog(document))) dialog.RunInteractionChecks(document, Check);
        using (var dialog = OffScreen(new PokedexDialog(document))) dialog.RunInteractionChecks(document, Check);
        document.Trainer.Edit = document.Trainer.Original with { Name = "Ana" }; document.Pokedex.Set(document.Pokedex.Entries[0], true, true);
        UpdateState(); ShowTrainer();
        Check(save.Enabled && Text.Contains($"{document.ToolChangeCount} ") && document.ToolChangeCount == 2 && trainer.Text.StartsWith("Ana"), "Tool changes enable Save and count in the window title");
        Check(document.HasChanges, "Tool changes make closing ask first");
        var toolOutput = document.BuildOutput();
        Check(SaveDocument.Hash(document.OriginalBytes) == document.OriginalHash && document.Save.OT != "Ana", "Preparing the file leaves the open game alone");
        Check(toolOutput.Length == document.OriginalBytes.Length, "Tool changes can be saved");
        document.Trainer.Reset(); document.Pokedex.Reset();
        UpdateState(); ShowTrainer();
        Check(!document.HasChanges && !save.Enabled, "Undoing the tools leaves nothing pending");
        CheckBatch(Check);
        CheckLayout(Check, "normal window");
        Window.Size = Window.MinimumSize; CheckLayout(Check, "minimum window");
        var fonts = Descendants(tabs).Select(c => (Control: c, Font: c.Font)).ToList();
        foreach (var item in fonts) item.Control.Font = new Font(item.Font.FontFamily, item.Font.SizeInPoints * 1.25f, item.Font.Style);
        CheckLayout(Check, "minimum window with larger text");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }

    static string ToolScratch(string reportPath)
    {
        var folder = Path.GetFullPath(reportPath) + ".tools";
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        return folder;
    }

    static void WaitFor(Func<bool> finished, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!finished())
        {
            if (clock.Elapsed > TimeSpan.FromMinutes(2)) throw new TimeoutException(what);
            Application.DoEvents(); Thread.Sleep(1);
        }
    }

    TWindow OpenFromMenu<TWindow>(string text) where TWindow : ToolWindow
    {
        toolsMenu.Items.Cast<ToolStripItem>().First(i => i.Text == text).PerformClick();
        var opened = (TWindow)tool!;
        WaitFor(() => !opened.Working, text);
        return opened;
    }

    // On copies of the save and with a backups folder of their own: the user's are out of reach from here.
    void CheckSaveTools(Action<bool, string> Check, string scratch)
    {
        string opened = document!.FilePath;
        scratchBackups = Path.Combine(scratch, "Backups");
        Check(toolsMenu.Items.Count >= 4, "The tools menu offers the whole-save tools");
        CheckToolPlacement(Check);

        var illegal = document.Pokemon[4];
        Pick(illegal); levelBox.Value = 1;
        search.Text = "bunnelby";
        var audit = OpenFromMenu<AuditWindow>(T("Auditoría de legalidad"));
        Check(audit.Owner == Window && audit.Right < 0 && audit.Bottom < 0, "A tool opens beside the editor, not on its own somewhere else");
        Check(audit.Rows.Count == document.Pokemon.Count && audit.StatusText.Contains(document.Pokemon.Count.ToString()), "The audit goes through every Pokémon and says so");
        int row = audit.RowOf(illegal);
        Check(row >= 0 && audit.Cell(row, 2).Length != 0, "The audit judges pending edits and gives its reasons");
        audit.SelectRow(row);
        Check(audit.DetailText.Length >= audit.Cell(row, 2).Length - 2, "Selecting a row shows the reasons in full");
        audit.PressRow(row);
        Check(current == illegal && tabs.SelectedIndex == DetailsTab && search.Text.Length == 0 && tool == audit, "Clicking a row shows that Pokémon in the editor and leaves the audit open");
        restoreSelected.PerformClick();
        audit.PressAgain();
        levelBox.Value = 1;
        WaitFor(() => !audit.Working, "the second audit");
        Check(audit.RowOf(illegal) < 0, "Checking again sees the undone edit, and what is edited while it works waits for the next check");
        restoreSelected.PerformClick();
        audit.Close();
        Check(tool == null, "Closing a tool lets go of it");

        toolsMenu.Items.Cast<ToolStripItem>().First(i => i.Text == T("Auditoría de legalidad")).PerformClick();
        var stopped = tool!; stopped.Close();
        WaitFor(() => stopped.IsDisposed, "closing a tool that was working");
        for (int i = 0; i < 50; i++) { Application.DoEvents(); Thread.Sleep(2); }
        Check(tool == null, "A tool can be closed while it works");

        var report = OpenFromMenu<BoxReportWindow>(T("Informe de cajas"));
        Check(report.RowCount == document.Pokemon.Count && report.Cell(0, 1) == document.Pokemon[0].SpeciesName, "The report has a row for every Pokémon");
        report.SortBy(3);
        var levels = Enumerable.Range(0, report.RowCount).Select(i => int.Parse(report.Cell(i, 3))).ToList();
        Check(levels.SequenceEqual(levels.Order()), "The report sorts numbers as numbers");
        report.SortBy(0);
        Check(report.RowOf(document.Pokemon[0]) == 0, "The report sorts places by position");
        var csv = Path.Combine(scratch, "informe.csv"); report.ExportTo(csv);
        Check(File.ReadAllLines(csv).Length == document.Pokemon.Count + 1 && report.StatusText.Contains(document.Pokemon.Count.ToString()), "The report exports to CSV");
        report.PressRow(1);
        Check(current == document.Pokemon[1], "Clicking a row of the report shows that Pokémon");
        tabs.SelectedIndex = 0;

        var other = Path.Combine(scratch, "otra");
        document.Money = document.Save.Money == 1234 ? 4321 : 1234;
        File.WriteAllBytes(other, document.BuildOutput());
        document.ResetResources();
        var compare = OpenFromMenu<CompareWindow>(T("Comparar partidas"));
        Check(tool == compare && report.IsDisposed, "Opening a tool closes the previous one");
        Check(compare.FirstPath == opened, "Comparing starts from the open save");
        compare.PressCompare(opened, other); WaitFor(() => !compare.Working, "the comparison");
        Check(compare.RowCount == 1 && compare.Cell(0, 0) == T("Dinero"), "The comparison finds the money that differs");
        var notSave = Path.Combine(scratch, "nada.txt"); File.WriteAllText(notSave, "x");
        compare.PressCompare(opened, notSave); WaitFor(() => !compare.Working, "the refused comparison");
        Check(compare.RowCount == 0 && compare.StatusText.Length != 0, "A file that isn't a save is refused with an explanation");
        compare.PressCompare(opened, ""); WaitFor(() => !compare.Working, "the empty comparison");
        Check(compare.StatusText == T("Elige dos partidas del mismo juego."), "Comparing needs two files");
        compare.Close();

        var copy = Path.Combine(scratch, "main"); File.Copy(opened, copy, true);
        LoadFile(copy);
        var before = document!.OriginalBytes;
        document.Money = document.Save.Money == 1234 ? 4321 : 1234;
        document.Write(copy, BackupRoot, _ => null);
        LoadFile(copy);
        var edited = File.ReadAllBytes(copy);
        var backups = OpenFromMenu<BackupWindow>(T("Administrar respaldos"));
        Check(backups.RowCount == 1 && backups.Cell(0, 2) == T("Dinero") && backups.Cell(0, 1).Length != 0, "The backups list shows game and what changed");
        Check(backups.CanPressRestore && backups.DetailText.Contains(copy), "Selecting a backup says which file it belongs to");
        backups.FindEmulator = _ => "Eden"; backups.Confirm = _ => true;
        backups.PressRestore();
        Check(backups.StatusText.Contains("Eden") && File.ReadAllBytes(copy).SequenceEqual(edited) && tool == backups, "Restoring is refused while the emulator is open");
        backups.FindEmulator = _ => null;
        string? question = null; backups.Confirm = text => { question = text; return false; };
        backups.PressRestore();
        Check(question?.Contains(copy) == true && File.ReadAllBytes(copy).SequenceEqual(edited), "Restoring asks first, naming the file, and No leaves it alone");
        document!.Money = 0;
        backups.Confirm = text => { question = text; return true; };
        backups.PressRestore();
        Check(question!.Contains(T("Los cambios sin guardar de esta partida se perderán.")), "Restoring warns about pending edits");
        Check(File.ReadAllBytes(copy).SequenceEqual(before) && document!.OriginalBytes.SequenceEqual(before) && !document.HasChanges, "Restoring the open save reloads it, byte for byte");
        Check(tool == null && SaveTools.ListBackups(BackupRoot).Count == 2, "Restoring keeps a copy of what it replaced, and the tool closes with the old document");
        var undo = OpenFromMenu<BackupWindow>(T("Administrar respaldos"));
        undo.FindEmulator = _ => null; undo.Confirm = _ => true;
        Check(undo.Cell(0, 2) == T("Copia hecha antes de restaurar otro respaldo") && undo.CanPressRestore, "The copy made before restoring is listed first and can be restored");
        document!.Money = 0;
        undo.PressRestore();
        Check(File.ReadAllBytes(copy).SequenceEqual(edited) && document!.OriginalBytes.SequenceEqual(edited) && !document.HasChanges && tool == null, "Restoring that copy brings the exact file back, and the editor with it");

        var keep = document; document = null;
        tool = null;
        var noSave = OpenFromMenu<BackupWindow>(T("Administrar respaldos"));
        Check(noSave.RowCount == 3, "Backups can be reached with no save open, in case it no longer opens");
        noSave.Close();
        toolsMenu.Items.Cast<ToolStripItem>().First(i => i.Text == T("Informe de cajas")).PerformClick();
        Check(tool == null && status.Text == T("Abre una partida primero."), "A tool that needs a save says so");
        document = keep;
        LoadFile(opened); scratchBackups = null;
        Check(document!.FilePath == opened && !document.HasChanges, "The checks leave the app on the save they started from");
    }

    // With rectangles and with windows that are never shown: a check must not put anything on the user's screen.
    void CheckToolPlacement(Action<bool, string> Check)
    {
        Size size = new(1060, 580), smallest = new(760, 420);
        var screen = new Rectangle(0, 0, 1920, 1040);
        Rectangle Placed(Rectangle editor, Rectangle area) => ToolWindow.Place(editor, size, smallest, area);
        Check(Placed(new(100, 100, 1280, 900), screen) == new Rectangle(296, 396, 1060, 580), "A tool sits over the bottom right corner of the editor");
        Check(screen.Contains(Placed(new(1700, 900, 1280, 900), screen)), "An editor hanging off the bottom right keeps its tool on screen");
        Check(screen.Contains(Placed(new(-1100, -700, 1280, 900), screen)), "An editor hanging off the top left keeps its tool on screen");
        var leftMonitor = new Rectangle(-1920, -200, 1920, 1080);
        Check(leftMonitor.Contains(Placed(new(-300, 700, 1280, 900), leftMonitor)), "A monitor with negative coordinates works the same");
        var small = new Rectangle(0, 0, 900, 500);
        Check(Placed(new(0, 0, 1200, 720), small) == small, "A tool shrinks to fit a small screen");
        var tiny = new Rectangle(10, 20, 640, 360);
        Check(Placed(tiny, tiny) == new Rectangle(10, 20, 760, 420), "Past its smallest size a tool keeps its title bar in reach");
        Check(Placed(new(-16000, -16000, 1280, 900), screen).Right < 0, "An editor off the screen takes its tool with it");
        var area = Screen.FromControl(Window).WorkingArea;
        using var corner = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(area.Right - 300, area.Bottom - 200, 1200, 720) };
        using var report = new BoxReportWindow(document!, ShowPokemon);
        report.MoveBeside(corner);
        Check(!corner.Visible && !report.Visible && Screen.FromControl(report).WorkingArea.Contains(report.Bounds), "A tool from an editor in the corner of the working area stays whole on it");
    }

    // Every tool, full of data, at its own size and at the smallest it allows.
    void CheckToolLayouts(Action<bool, string> Check, string reportPath)
    {
        string scratch = ToolScratch(reportPath);
        scratchBackups = Path.Combine(scratch, "Backups");
        var copy = Path.Combine(scratch, "main"); File.Copy(document!.FilePath, copy, true);
        var written = SaveDocument.Read(copy);
        written.Money = written.Save.Money == 1234 ? 4321 : 1234; written.Pokemon[0].Edit.Level = written.Pokemon[0].Edit.Level == 50 ? 51 : 50;
        written.Write(copy, BackupRoot, _ => null);
        void Inspect(ToolWindow window, string name)
        {
            foreach (var (label, size) in new[] { ("normal", window.Size), ("minimum", window.MinimumSize) })
            {
                window.Size = size; window.PerformLayout(); Application.DoEvents();
                var clipped = Clipped(window);
                Check(clipped.Length == 0, $"Every text fits ({name}, {label} window) {clipped}");
                Check(window.ButtonsFit, $"Every button fits ({name}, {label} window)");
                using var picture = Theme.Capture(window);
                picture.Save($"{reportPath}.{name}-{label}.png", System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        Inspect(OpenFromMenu<AuditWindow>(T("Auditoría de legalidad")), "audit");
        Inspect(OpenFromMenu<BoxReportWindow>(T("Informe de cajas")), "report");
        var backups = OpenFromMenu<BackupWindow>(T("Administrar respaldos"));
        Inspect(backups, "backups");
        var compare = OpenFromMenu<CompareWindow>(T("Comparar partidas"));
        compare.PressCompare(copy, document.FilePath); WaitFor(() => !compare.Working, "the comparison");
        Check(compare.RowCount != 0, "The comparison shows something to lay out");
        Inspect(compare, "compare");
        compare.Close(); scratchBackups = null;
    }

    internal void OpenToolForChecks() => toolsMenu.Items[0].PerformClick();

    void CheckLayout(Action<bool, string> Check, string sizeLabel)
    {
        tabs.SelectedIndex = 0; PerformLayout(); Application.DoEvents();
        var outside = Descendants(this).OfType<Button>().FirstOrDefault(b => b.Visible && b.Parent is FlowLayoutPanel && b.Left < 0);
        Check(outside == null, $"Every button of the header and the footer fits ({sizeLabel}) {outside?.Text}");
        var grid = (TableLayoutPanel)ivs[0].Parent!;
        for (int i = 0; i < 6; i++)
        {
            var label = grid.GetControlFromPosition(0, i + 1)!;
            double labelCenter = label.Top + label.Height / 2d;
            Check(ivs[i].Height >= ivs[i].PreferredHeight && evs[i].Height >= evs[i].PreferredHeight
                && Math.Abs(labelCenter - (ivs[i].Top + ivs[i].Height / 2d)) <= 2
                && Math.Abs(labelCenter - (evs[i].Top + evs[i].Height / 2d)) <= 2,
                $"IV and EV fields align with {Catalog.Stats[i]} ({sizeLabel})");
            if (i > 0) Check(ivs[i].Top >= ivs[i - 1].Bottom + 6 && evs[i].Top >= evs[i - 1].Bottom + 6,
                $"Stat rows have space between fields ({sizeLabel}, row {i + 1})");
        }
        var natureLabel = ((TableLayoutPanel)nature.Parent!).GetControlFromPosition(0, 0)!;
        Check(natureLabel.Width >= natureLabel.GetPreferredSize(Size.Empty).Width, $"Nature label is not truncated ({sizeLabel})");
        Check(!abilityNote.Visible || abilityNote.Width >= abilityNote.PreferredWidth, $"Ability note fits on its line ({sizeLabel}; {abilityNote.Width} of {abilityNote.PreferredWidth})");
        var statsPage = tabs.TabPages[0];
        var cut = Descendants(statsPage).FirstOrDefault(c => c.Visible && statsPage.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)).Right > statsPage.ClientSize.Width);
        if (!sizeLabel.Contains("larger text")) Check(cut == null, $"Nothing is cut off on the right of the stats tab ({sizeLabel}; {cut?.GetType().Name} {cut?.Text} {cut?.Bounds} of {cut?.Parent?.GetType().Name} {cut?.Parent?.ClientSize}, page {statsPage.ClientSize}, scroll {statsPage.VerticalScroll.Visible})");
        Check(!boxGrid.VerticalScroll.Visible, $"A whole box fits without scrolling ({sizeLabel})");
        tabs.SelectedIndex = 1; PerformLayout(); Application.DoEvents();
        var moveGrid = (TableLayoutPanel)moves[0].Parent!;
        for (int i = 0; i < 4; i++)
        {
            var label = moveGrid.GetControlFromPosition(0, i)!;
            Check(label.Width >= label.GetPreferredSize(Size.Empty).Width
                && Math.Abs((label.Top + label.Height / 2d) - (moves[i].Top + moves[i].Height / 2d)) <= 2,
                $"Movement {i + 1} label and field align without truncation ({sizeLabel}; label {label.Width}x{label.Height}, preferred {label.GetPreferredSize(Size.Empty)}, center {label.Top + label.Height / 2d}; field {moves[i].Width}x{moves[i].Height}, center {moves[i].Top + moves[i].Height / 2d})");
        }
        if (sizeLabel.Contains("larger text")) return;
        for (int page = 0; page < tabs.TabCount; page++)
        {
            tabs.SelectedIndex = page; PerformLayout(); Application.DoEvents();
            var clipped = Clipped(this);
            Check(clipped.Length == 0, $"Every text fits ({sizeLabel}, tab {page}) {clipped}");
            if (page == 2) Check(resources.VisibleItemRows >= 3, $"The item list shows several rows ({sizeLabel}: {resources.VisibleItemRows})");
        }
        tabs.SelectedIndex = 0;
        CheckTools(Check, sizeLabel);
        CheckBatchLayout(Check, sizeLabel);
    }

    static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));

    // Text that doesn't fit its control, in whatever language is on screen.
    internal static string Clipped(Control root) => string.Join(" | ", Descendants(root).Where(c => {
        if (!c.Visible || c.Text.Length == 0 || c is not (Label { AutoEllipsis: false } or ButtonBase)) return false;
        var size = TextRenderer.MeasureText(c.Text, c.Font, new Size(c.ClientSize.Width, int.MaxValue), TextFormatFlags.WordBreak);
        return size.Height > c.ClientSize.Height + 1 || size.Width > c.ClientSize.Width + 1;
    }).Select(c => $"{c.Text.Split('\n')[0]} ({c.ClientSize.Width}x{c.ClientSize.Height})"));

    /// <summary>The same rules in any language or theme, with nothing to do with what the save contains.</summary>
    public void RunLayoutChecks(string reportPath, AppSettings expected)
    {
        var checks = new List<string>();
        void Check(bool ok, string description) { if (!ok) throw new Exception(description); checks.Add(description); }
        Check(document != null && Strings.Current.Code == expected.Language && Theme.IsDark == expected.Dark, "The window opens in the chosen language and theme");
        CheckZaTools(Check);
        CheckLayout(Check, "normal window");
        Window.Size = Window.MinimumSize; CheckLayout(Check, "minimum window");
        CheckToolLayouts(Check, reportPath);
        CheckBoxMessages(Check);
        CheckBatchMessages(Check, reportPath);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }

    // Shown out of sight, because a window placed by Windows would land on the user's screen.
    void CheckZaTools(Action<bool, string> Check)
    {
        bool isZA = document!.Rules.IsZA;
        Check(zaTool?.Available == isZA && toolsButton.Visible == toolsMenu.Items.Cast<ToolStripItem>().Any(item => item.Available), "The Z-A tools are in the menu only with a Z-A save open");
        if (!isZA) return;
        using var dialog = new ZaToolsDialog(document) { StartPosition = FormStartPosition.Manual, Location = new Point(-16000, -16000) };
        dialog.Show(Window);
        dialog.RunChecks(Check, Clipped);
        using (var blocked = new ZaToolsDialog(document, new ZaEdits(document, document.ZaSave!, donutBlock: 0, fieldItemBlock: 0)) { StartPosition = FormStartPosition.Manual, Location = new Point(-16000, -16000) })
        {
            blocked.Show(Window);
            blocked.RunBlockedChecks(Check, Clipped);
            blocked.Close();
        }
        resources.RefreshDraft(); UpdateState();
        Check(!document.HasChanges && !save.Enabled, "Undoing the Z-A tools leaves nothing to save");
        document.Za!.Points[0].Value++; UpdateState();
        Check(document.HasChanges && save.Enabled && Text.EndsWith(T(" · {0} cambios pendientes", 1)), "What the Z-A tools prepare counts as a pending change");
        document.Za.Reset(); UpdateState();
    }

    internal void PressSettings() => settingsButton.PerformClick();

    /// <summary>Leaves changes pending, on a tab and a box other than the first, for a settings change to keep.</summary>
    internal void StagePendingEdits()
    {
        Pick(document!.Pokemon.First(p => !p.IsParty)); ivs[0].Value = ivs[0].Value == 13 ? 14 : 13;
        document.SetItemCount(1128, 7); tabs.SelectedIndex = 1;
        document.Za?.AddDonuts([ZaEdits.TopPowers()[0].Hash], 1);
        document.Clone(document.Pokemon[0], document.FreePlace(box)!.Value);
    }

    internal void CheckResumed(Snapshot before, string saveText, string itemName, Action<bool, string> Check)
    {
        var edited = current!; var open = document!;
        Check(open == before.Document && edited == before.Entry && edited.Dirty && open.GetItemCount(1128) == 7, "Pending edits survive the change");
        Check(open.Za?.DonutsChanged != false, "What the Z-A tools prepared survives the change");
        Check(tabs.SelectedIndex == before.Tab && box == before.Box && box == open.BoxOf(edited), "The tab and the open box survive the change");
        Check(open.Pokemon.Any(p => p.IsNew) && open.CanUndoLayout, "A pending box change survives the change");
        Check(save.Text == saveText, $"The new view speaks the new language ({saveText})");
        Check(open.Items.First(i => i.Id == 1128).Name == itemName, $"Item names follow the language ({itemName})");
        Check(open.ValidateEdits() == null && open.BuildOutput().Length == open.OriginalBytes.Length, "The save can still be written");
        CheckLayout(Check, "after changing the settings");
        tabs.SelectedIndex = before.Tab;
    }
}
