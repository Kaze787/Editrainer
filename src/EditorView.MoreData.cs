using System.ComponentModel;
using PKHeX.Core;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>A date as three numbers, so that a day that doesn't exist can be typed and refused with a reason.</summary>
sealed class DateFields : TableLayoutPanel
{
    const int YearWidth = 78, DayWidth = 60;
    readonly NumericUpDown year = Part(GameDate.FirstYear, YearWidth), month = Part(0, DayWidth), day = Part(0, DayWidth);
    public event EventHandler? DateChanged;

    public DateFields()
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; ColumnCount = 3; RowCount = 1; Margin = Padding.Empty; Anchor = AnchorStyles.Left;
        month.Maximum = 12; day.Maximum = 31;
        Controls.Add(year, 0, 0); Controls.Add(month, 1, 0); Controls.Add(day, 2, 0);
        foreach (var part in new[] { year, month, day }) part.ValueChanged += (_, _) => DateChanged?.Invoke(this, EventArgs.Empty);
    }

    static NumericUpDown Part(int minimum, int width) => new() {
        Minimum = minimum, Maximum = 9999, Width = width, Font = Theme.Font(10), TextAlign = HorizontalAlignment.Center,
        BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 6, 4, 4), Anchor = AnchorStyles.Left,
    };

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GameDate Value
    {
        get => new((int)year.Value, (int)month.Value, (int)day.Value);
        set
        {
            year.Value = Math.Clamp(value.Year, year.Minimum, year.Maximum);
            month.Value = Math.Clamp(value.Month, month.Minimum, month.Maximum);
            day.Value = Math.Clamp(value.Day, day.Minimum, day.Maximum);
        }
    }

    public void Describe(ToolTip tips, string text)
    {
        tips.SetToolTip(year, text); tips.SetToolTip(month, text); tips.SetToolTip(day, text);
    }
}

/// <summary>The tab for what a Pokémon keeps beyond its stats and moves. Each game shows only what it stores.</summary>
public sealed partial class EditorView
{
    const int MoreDataTab = 4, LocationWidth = 420, ListHeight = 184, PPRows = 2, PPColumns = 2, MarkingSize = 38, SmallBoxWidth = 84, StatTitleSize = 8, StatTitlePadding = 8;
    const string CircleToDiamond = "●▲■♥★◆";
    readonly TableLayoutPanel moreLayout = Table(1, 0);
    readonly Label moreNote = Theme.Label("", 9, color: Theme.Muted);
    readonly List<Action<PokemonEntry>> moreBinders = [];
    // Which game shows what, so a new game only has to answer the same questions.
    readonly List<(Func<GameRules, bool> Shows, Control[] Controls)> moreGroups = [];
    readonly ComboBox gamePicker = Theme.Combo(), languagePicker = Theme.Combo(), trainerGenderPicker = Theme.Combo(), teraPicker = Theme.Combo();
    readonly MovePicker metPlacePicker = new(), eggPlacePicker = new();
    readonly MovePicker[] relearnPickers = new MovePicker[MoreData.MoveCount];
    readonly DateFields metDateFields = new(), eggDateFields = new();
    readonly Label metPlaceLabel = FieldLabel(T("Lugar de encuentro")), metDateLabel = FieldLabel(T("Fecha de encuentro"));
    readonly Label eggPlaceLabel = FieldLabel(T("Lugar del huevo")), eggDateLabel = FieldLabel(T("Fecha del huevo"));
    readonly Label[] ppLabels = new Label[MoreData.MoveCount];
    readonly NumericUpDown[] ppBoxes = new NumericUpDown[MoreData.MoveCount];
    readonly CheckBox[] hyperBoxes = new CheckBox[MoreData.StatCount];
    readonly NumericUpDown[] gvBoxes = new NumericUpDown[MoreData.StatCount];
    readonly Button[] markingButtons = new Button[CircleToDiamond.Length];
    readonly CheckedListBox ribbonList = RibbonList(), markList = RibbonList();
    readonly TableLayoutPanel countedRibbons = Table(4, 0);
    readonly List<(RibbonChoice Ribbon, NumericUpDown Box)> countedBoxes = [];
    NumericUpDown metLevelBox = null!, pokerusStrainBox = null!, pokerusDaysBox = null!, dynamaxBox = null!, heightBox = null!, weightBox = null!, scaleBox = null!;
    readonly NumericUpDown[] contestBoxes = new NumericUpDown[MoreData.StatCount];
    CheckBox gigantamaxBox = null!, alphaBox = null!;

    static CheckedListBox RibbonList() => new() {
        Font = Theme.Font(9), Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true, MinimumSize = new Size(0, ListHeight),
        BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface, ForeColor = Theme.Ink, Margin = new Padding(3, 3, 3, 8),
    };

    static Label Heading(string text)
    {
        var label = Theme.Label(text, 9, true, Theme.Muted); label.AutoSize = true; label.Margin = new Padding(3, 12, 3, 4);
        return label;
    }

    static CheckBox MoreCheck(string text) => new() { Text = text, AutoSize = true, Font = Theme.Font(10, true), ForeColor = Theme.Ink, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 6, 6) };

    static TableLayoutPanel Fields(int columns, params float[] percents)
    {
        var table = Table(columns, 0); table.Dock = DockStyle.Top; table.AutoSize = true; table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        for (int i = 0; i < columns; i++)
            table.ColumnStyles.Add(percents.Length == 0 ? new(SizeType.AutoSize) : percents[i] == 0 ? new(SizeType.AutoSize) : new(SizeType.Percent, percents[i]));
        return table;
    }

    // Adds a row of label and field pairs, or anything else, taking each column in turn.
    static void AddRow(TableLayoutPanel table, params Control[] cells)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new(SizeType.AutoSize));
        for (int i = 0; i < cells.Length; i++) table.Controls.Add(cells[i], i, row);
        if (cells.Length != 0) table.SetColumnSpan(cells[^1], table.ColumnCount - cells.Length + 1);
    }

    void Show(Func<GameRules, bool> shows, params Control[] controls) => moreGroups.Add((shows, controls));

    // Each field writes straight to the draft, and reads it back whenever the tab shows a Pokémon.
    NumericUpDown MoreNumber(int max, Func<MoreData, int> read, Action<MoreData, int> write)
    {
        var box = Number(max); box.Anchor = AnchorStyles.Left; box.Width = SmallBoxWidth; box.Margin = new Padding(4, 6, 8, 4);
        box.ValueChanged += (_, _) => { if (!binding && current != null) { write(current.Edit.More, (int)box.Value); UpdateState(); } };
        moreBinders.Add(e => box.Value = Math.Clamp(read(e.Edit.More), box.Minimum, box.Maximum));
        return box;
    }

    CheckBox MoreFlag(string text, Func<MoreData, bool> read, Action<MoreData, bool> write)
    {
        var box = MoreCheck(text);
        box.CheckedChanged += (_, _) => { if (!binding && current != null) { write(current.Edit.More, box.Checked); UpdateState(); } };
        moreBinders.Add(e => box.Checked = read(e.Edit.More));
        return box;
    }

    static void FillCombo(ComboBox combo, IEnumerable<Choice> choices, int selected)
    {
        var list = choices.ToList();
        combo.Items.Clear(); combo.Items.AddRange(list.Cast<object>().ToArray());
        combo.SelectedItem = list.FirstOrDefault(c => c.Id == selected);
    }

    void BuildMoreDataTab()
    {
        var page = new TabPage(T("Más datos")) { BackColor = Theme.Surface, Padding = new Padding(12, 6, 12, 4), AutoScroll = true };
        tabs.TabPages.Add(page);
        moreLayout.Dock = DockStyle.Top; moreLayout.AutoSize = true; moreLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink; moreLayout.Visible = false;
        moreLayout.ColumnStyles.Add(new(SizeType.Percent, 100));
        page.Controls.Add(moreLayout);
        moreNote.AutoSize = true; moreNote.Margin = new Padding(3, 4, 3, 2); moreNote.Visible = false;
        AddRow(moreLayout, moreNote);
        BuildOriginSection(); BuildMoveSection(); BuildTrainingSection(); BuildStateSection(); BuildRibbonSection();
    }

    void BuildOriginSection()
    {
        var heading = Heading(T("Origen"));
        Show(r => r.HasMetPlace, heading);
        AddRow(moreLayout, heading);
        var table = Fields(4, 0, 50, 0, 50);
        var gameLabel = FieldLabel(T("Juego de origen")); var languageLabel = FieldLabel(T("Idioma"));
        AddRow(table, gameLabel, gamePicker, languageLabel, languagePicker);
        Show(r => r.HasOriginGame, gameLabel, gamePicker, languageLabel, languagePicker);
        gamePicker.SelectedIndexChanged += (_, _) => {
            if (binding || current == null || gamePicker.SelectedItem is not Choice game) return;
            Catalog.SetGame(current, game.Id); ShowPlaces(current); UpdateState();
        };
        languagePicker.SelectedIndexChanged += (_, _) => {
            if (binding || current == null || languagePicker.SelectedItem is not Choice language) return;
            Catalog.SetLanguage(current, language.Id);
            binding = true; nicknameBox.Text = current.Edit.Nickname; binding = false;
            UpdateState();
        };
        metPlacePicker.MaximumSize = eggPlacePicker.MaximumSize = new Size(LocationWidth, 0);
        AddRow(table, metPlaceLabel, metPlacePicker);
        Show(r => r.HasMetPlace, metPlaceLabel, metPlacePicker);
        metPlacePicker.MoveCommitted += (_, _) => { if (!binding && current != null && metPlacePicker.SelectedChoice is { } place) { current.Edit.More.MetLocation = place.Id; UpdateState(); } };
        eggPlacePicker.MoveCommitted += (_, _) => { if (!binding && current != null && eggPlacePicker.SelectedChoice is { } place) { current.Edit.More.EggLocation = place.Id; UpdateState(); } };
        var levelLabel = FieldLabel(T("Nivel de encuentro")); var genderLabel = FieldLabel(T("Género del entrenador"));
        metLevelBox = MoreNumber(Experience.MaxLevel, d => d.MetLevel, (d, v) => d.MetLevel = v);
        AddRow(table, levelLabel, metLevelBox, genderLabel, trainerGenderPicker);
        Show(r => r.HasMetLevel, levelLabel, metLevelBox);
        Show(r => r.HasTrainerGender, genderLabel, trainerGenderPicker);
        trainerGenderPicker.Items.AddRange([new Choice(0, T("Masculino")), new Choice(1, T("Femenino"))]);
        trainerGenderPicker.SelectedIndexChanged += (_, _) => { if (!binding && current != null && trainerGenderPicker.SelectedItem is Choice gender) { current.Edit.More.TrainerGender = gender.Id; UpdateState(); } };
        moreBinders.Add(e => trainerGenderPicker.SelectedIndex = e.Edit.More.TrainerGender is 0 or 1 ? e.Edit.More.TrainerGender : -1);
        AddRow(table, metDateLabel, metDateFields);
        Show(r => r.HasDates, metDateLabel, metDateFields);
        metDateFields.DateChanged += (_, _) => { if (!binding && current != null) { current.Edit.More.MetDate = metDateFields.Value; UpdateState(); } };
        moreBinders.Add(e => metDateFields.Value = e.Edit.More.MetDate);
        AddRow(table, eggPlaceLabel, eggPlacePicker);
        AddRow(table, eggDateLabel, eggDateFields);
        Show(r => r.HasEggData, eggPlaceLabel, eggPlacePicker, eggDateLabel, eggDateFields);
        eggDateFields.DateChanged += (_, _) => { if (!binding && current != null) { current.Edit.More.EggDate = eggDateFields.Value; UpdateState(); } };
        moreBinders.Add(e => eggDateFields.Value = e.Edit.More.EggDate);
        AddRow(moreLayout, table);
        Show(r => r.HasMetPlace, table);
        moreBinders.Add(e => {
            if (!e.Rules.HasOriginGame) { ShowPlaces(e); return; }
            FillCombo(gamePicker, Catalog.GetGames(e), e.Edit.More.Game);
            FillCombo(languagePicker, Catalog.GetLanguages(e), e.Edit.More.Language);
            ShowPlaces(e);
        });
        tips.SetToolTip(gamePicker, T("Cambiar de juego actualiza la lista de lugares."));
        tips.SetToolTip(languagePicker, T("Un Pokémon sin apodo toma el nombre de su especie en este idioma."));
        tips.SetToolTip(metPlacePicker, T("Escribe parte del nombre para buscar. Enter confirma y Esc cancela."));
        tips.SetToolTip(eggPlacePicker, T("Dónde se recibió el huevo. El lugar de eclosión está arriba."));
        metDateFields.Describe(tips, T("Año, mes y día."));
        eggDateFields.Describe(tips, T("Año, mes y día."));
    }

    // The places depend on the game being edited, and the egg's only matter to a Pokémon that was one.
    void ShowPlaces(PokemonEntry e)
    {
        bool wasBinding = binding; binding = true;
        var rules = e.Rules;
        if (rules.HasMetPlace) metPlacePicker.SetChoices(Catalog.GetLocations(e, false), e.Edit.More.MetLocation);
        bool wasEgg = rules.HasEggData && e.Original.WasEgg;
        if (wasEgg) eggPlacePicker.SetChoices(Catalog.GetLocations(e, true), e.Edit.More.EggLocation);
        eggPlaceLabel.Visible = eggPlacePicker.Visible = eggDateLabel.Visible = eggDateFields.Visible = wasEgg;
        metPlaceLabel.Text = wasEgg ? T("Lugar de eclosión") : T("Lugar de encuentro");
        metDateLabel.Text = wasEgg ? T("Fecha de eclosión") : T("Fecha de encuentro");
        binding = wasBinding;
    }

    void BuildMoveSection()
    {
        var ppHeading = Heading(T("PP extra"));
        var ppTable = Fields(PPColumns * 2, 50, 0, 50, 0);
        for (int row = 0; row < PPRows; row++)
        {
            var cells = new List<Control>();
            for (int column = 0; column < PPColumns; column++)
            {
                int slot = row * PPColumns + column;
                ppLabels[slot] = Theme.Label("", 9, true);
                ppBoxes[slot] = Number(MoreData.MaxPPUps); ppBoxes[slot].Anchor = AnchorStyles.Left; ppBoxes[slot].Width = SmallBoxWidth; ppBoxes[slot].Margin = new Padding(4, 6, 12, 4);
                ppBoxes[slot].ValueChanged += (_, _) => {
                    if (binding || current == null) return;
                    current.Edit.More.PPUps[slot] = new(current.Edit.Moves[slot], (int)ppBoxes[slot].Value); UpdateState();
                };
                cells.Add(ppLabels[slot]); cells.Add(ppBoxes[slot]);
            }
            AddRow(ppTable, [.. cells]);
        }
        AddRow(moreLayout, ppHeading); AddRow(moreLayout, ppTable);
        Show(r => r.HasPPUps, ppHeading, ppTable);
        moreBinders.Add(ShowPPUps);

        var relearnHeading = Heading(T("Movimientos recordables"));
        var relearnTable = Fields(2, 50, 50);
        for (int i = 0; i < MoreData.MoveCount; i++)
        {
            int slot = i;
            relearnPickers[i] = new MovePicker();
            relearnPickers[i].MoveCommitted += (_, _) => {
                if (!binding && current != null && relearnPickers[slot].SelectedChoice is { } move) { current.Edit.More.Relearn[slot] = (ushort)move.Id; UpdateState(); }
            };
        }
        AddRow(relearnTable, relearnPickers[0], relearnPickers[1]); AddRow(relearnTable, relearnPickers[2], relearnPickers[3]);
        AddRow(moreLayout, relearnHeading); AddRow(moreLayout, relearnTable);
        Show(r => r.HasRelearn, relearnHeading, relearnTable);
        moreBinders.Add(e => {
            if (!e.Rules.HasRelearn) return;
            var choices = Catalog.GetRelearnMoves(e);
            for (int i = 0; i < MoreData.MoveCount; i++) relearnPickers[i].SetChoices(choices, e.Edit.More.Relearn[i]);
        });
        tips.SetToolTip(ppHeading, T("Cada aumento suma un 20 % de los PP base. Cambiar el movimiento deja sus PP extra en 0."));
    }

    void ShowPPUps(PokemonEntry e)
    {
        for (int i = 0; i < MoreData.MoveCount; i++)
        {
            ushort move = e.Edit.Moves[i];
            ppLabels[i].Text = move == 0 ? T("— Sin movimiento —") : Catalog.Names.movelist[move];
            ppBoxes[i].Value = Math.Clamp(e.Edit.PPUpsOf(i), 0, MoreData.MaxPPUps);
            ppBoxes[i].Enabled = Legal.IsPPUpAvailable(move);
        }
    }

    void BuildTrainingSection()
    {
        var heading = Heading(T("Entrenamiento"));
        var table = Fields(MoreData.StatCount + 1, 0, 16, 16, 17, 17, 17, 17);
        AddTitles(table, [Theme.Label("", 9), .. Catalog.Stats.Select(StatTitle)]);
        var hyperLabel = FieldLabel(T("Entrenamiento máximo")); var gvLabel = FieldLabel(T("GV"));
        var boxes = new List<Control> { hyperLabel };
        for (int i = 0; i < MoreData.StatCount; i++)
        {
            int stat = i;
            var box = hyperBoxes[i] = new CheckBox { AutoSize = true, Anchor = AnchorStyles.None, AccessibleName = Catalog.Stats[i], Margin = new Padding(3, 8, 3, 6) };
            box.CheckedChanged += (_, _) => { if (!binding && current != null) { current.Edit.More.HyperTraining[stat] = box.Checked; UpdateState(); } };
            moreBinders.Add(e => box.Checked = e.Edit.More.HyperTraining[stat]);
            boxes.Add(box);
        }
        AddRow(table, [.. boxes]);
        var numbers = new List<Control> { gvLabel };
        for (int i = 0; i < MoreData.StatCount; i++)
        {
            int stat = i;
            var box = gvBoxes[i] = MoreNumber(GanbaruExtensions.TrueMax, d => d.GVs[stat], (d, v) => d.GVs[stat] = v);
            box.Width = 72; box.Margin = new Padding(2, 6, 2, 4); box.Anchor = AnchorStyles.None;
            moreBinders.Insert(moreBinders.Count - 1, e => { if (e.Rules.HasGVs) box.Maximum = Math.Max(Catalog.MaxGV(e, stat), e.Baseline.More.GVs[stat]); });
            numbers.Add(box);
        }
        AddRow(table, [.. numbers]);
        AddRow(moreLayout, heading); AddRow(moreLayout, table);
        Show(r => r.HasHyperTraining || r.HasGVs, heading, table);
        Show(r => r.HasHyperTraining, [hyperLabel, .. hyperBoxes]);
        Show(r => r.HasGVs, [gvLabel, .. gvBoxes]);
        tips.SetToolTip(hyperLabel, T("Hace que la estadística cuente como si su IV fuera 31, sin cambiar el IV."));

        var contestHeading = Heading(T("Concurso"));
        var contest = Fields(MoreData.StatCount, 16, 16, 17, 17, 17, 17);
        string[] names = [T("Carisma"), T("Belleza"), T("Dulzura"), T("Ingenio"), T("Dureza"), T("Brillo")];
        AddTitles(contest, [.. names.Select(StatTitle)]);
        for (int i = 0; i < MoreData.StatCount; i++)
        {
            int stat = i;
            contestBoxes[i] = MoreNumber(MoreData.MaxContest, d => d.Contest[stat], (d, v) => d.Contest[stat] = v);
            contestBoxes[i].Anchor = AnchorStyles.None;
        }
        AddRow(contest, [.. contestBoxes]);
        AddRow(moreLayout, contestHeading); AddRow(moreLayout, contest);
        Show(r => r.HasContest, contestHeading, contest);
    }

    // Two lines tall, fixed: an automatic row measures these titles at a width they do not end up with, and squeezes the row below.
    static void AddTitles(TableLayoutPanel table, Control[] titles)
    {
        AddRow(table, titles);
        using var font = Theme.Font(StatTitleSize, true);
        table.RowStyles[^1] = new(SizeType.Absolute, TextRenderer.MeasureText("A\nA", font).Height + StatTitlePadding);
    }

    static Label StatTitle(string text)
    {
        var label = Theme.Label(text, StatTitleSize, true, Theme.Muted);
        label.TextAlign = ContentAlignment.MiddleCenter; label.Margin = new Padding(1, 2, 1, 2);
        return label;
    }

    void BuildStateSection()
    {
        var heading = Heading(T("Estado"));
        AddRow(moreLayout, heading);
        Show(r => r.HasPokerus || r.HasTera || r.HasDynamax || r.HasSize || r.HasAlpha || r.HasMarkings, heading);
        var table = Fields(4, 0, 50, 0, 50);
        var strainLabel = FieldLabel(T("Cepa del Pokérus")); var daysLabel = FieldLabel(T("Días de contagio"));
        pokerusStrainBox = MoreNumber(15, d => d.PokerusStrain, (d, v) => d.PokerusStrain = v);
        pokerusDaysBox = MoreNumber(4, d => d.PokerusDays, (d, v) => d.PokerusDays = v);
        AddRow(table, strainLabel, pokerusStrainBox, daysLabel, pokerusDaysBox);
        Show(r => r.HasPokerus, strainLabel, pokerusStrainBox, daysLabel, pokerusDaysBox);
        tips.SetToolTip(pokerusDaysBox, T("Con una cepa y 0 días, el Pokémon ya está curado y conserva el efecto."));
        var teraLabel = FieldLabel(T("Teratipo"));
        AddRow(table, teraLabel, teraPicker);
        Show(r => r.HasTera, teraLabel, teraPicker);
        teraPicker.SelectedIndexChanged += (_, _) => { if (!binding && current != null && teraPicker.SelectedItem is Choice tera) { current.Edit.More.TeraType = tera.Id; UpdateState(); } };
        moreBinders.Add(e => {
            if (!e.Rules.HasTera) return;
            FillCombo(teraPicker, Catalog.GetTeraTypes(), e.Edit.More.TeraType);
            teraPicker.Enabled = Catalog.CanEditTera(e);
        });
        var dynamaxLabel = FieldLabel(T("Nivel Dinamax"));
        dynamaxBox = MoreNumber(MoreData.MaxDynamaxLevel, d => d.DynamaxLevel, (d, v) => d.DynamaxLevel = v);
        gigantamaxBox = MoreFlag(T("Gigamax"), d => d.Gigantamax, (d, v) => d.Gigantamax = v);
        AddRow(table, dynamaxLabel, dynamaxBox, gigantamaxBox);
        table.SetColumnSpan(gigantamaxBox, 2);
        Show(r => r.HasDynamax, dynamaxLabel, dynamaxBox, gigantamaxBox);
        moreBinders.Add(e => {
            if (!e.Rules.HasDynamax) return;
            dynamaxBox.Enabled = Catalog.CanHaveDynamax(e);
            gigantamaxBox.Visible = Catalog.CanGigantamax(e);
        });
        var heightLabel = FieldLabel(T("Altura")); var weightLabel = FieldLabel(T("Peso")); var scaleLabel = FieldLabel(T("Escala"));
        heightBox = MoreNumber(MoreData.MaxScalar, d => d.Height, (d, v) => d.Height = v);
        weightBox = MoreNumber(MoreData.MaxScalar, d => d.Weight, (d, v) => d.Weight = v);
        scaleBox = MoreNumber(MoreData.MaxScalar, d => d.Scale, (d, v) => d.Scale = v);
        AddRow(table, heightLabel, heightBox, weightLabel, weightBox);
        Show(r => r.HasSize, heightLabel, heightBox, weightLabel, weightBox);
        tips.SetToolTip(heightBox, T("De 0 a 255: 0 es el más pequeño posible y 255 el más grande."));
        tips.SetToolTip(weightBox, T("De 0 a 255: 0 es el más pequeño posible y 255 el más grande."));
        alphaBox = MoreFlag(T("Alfa"), d => d.Alpha, (d, v) => d.Alpha = v);
        AddRow(table, scaleLabel, scaleBox, alphaBox);
        table.SetColumnSpan(alphaBox, 2);
        Show(r => r.HasScale, scaleLabel, scaleBox);
        Show(r => r.HasAlpha, alphaBox);
        var markingLabel = FieldLabel(T("Marcas de la mochila"));
        var markings = Flow(); markings.AutoSize = true; markings.Dock = DockStyle.Top; markings.WrapContents = false;
        for (int i = 0; i < markingButtons.Length; i++)
        {
            int index = i;
            var button = markingButtons[i] = Theme.Button(CircleToDiamond[i].ToString());
            button.AutoSize = false; button.MinimumSize = Size.Empty; button.Padding = Padding.Empty; button.Font = Theme.Font(12);
            button.Size = new Size(MarkingSize, MarkingSize - 4); button.Margin = new Padding(2, 4, 2, 4); button.AccessibleName = T("Marcas de la mochila");
            button.Click += (_, _) => {
                if (current == null) return;
                var values = current.Edit.More.Markings;
                values[index] = (values[index] + 1) % (current.Rules.MaxMarking + 1);
                ShowMarkings(current); UpdateState();
            };
            markings.Controls.Add(button);
            tips.SetToolTip(button, T("Pulsa para cambiar esta marca."));
        }
        AddRow(table, markingLabel, markings);
        Show(r => r.HasMarkings, markingLabel, markings);
        moreBinders.Add(ShowMarkings);
        AddRow(moreLayout, table);
    }

    void ShowMarkings(PokemonEntry e)
    {
        var values = e.Edit.More.Markings;
        for (int i = 0; i < markingButtons.Length; i++)
        {
            var button = markingButtons[i];
            button.Visible = i < values.Length;
            if (!button.Visible) continue;
            button.ForeColor = values[i] switch { 0 => Theme.Muted, (int)MarkingColor.Pink => Theme.MarkingPink, _ => e.Rules.HasColoredMarkings ? Theme.MarkingBlue : Theme.Ink };
            button.BackColor = values[i] == 0 ? Theme.Surface : Theme.AccentLight;
        }
    }

    void BuildRibbonSection()
    {
        var ribbonHeading = Heading(T("Cintas")); var markHeading = Heading(T("Emblemas"));
        countedRibbons.Dock = DockStyle.Top; countedRibbons.AutoSize = true; countedRibbons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        countedRibbons.ColumnStyles.Add(new(SizeType.AutoSize)); countedRibbons.ColumnStyles.Add(new(SizeType.Percent, 50));
        countedRibbons.ColumnStyles.Add(new(SizeType.AutoSize)); countedRibbons.ColumnStyles.Add(new(SizeType.Percent, 50));
        AddRow(moreLayout, ribbonHeading); AddRow(moreLayout, countedRibbons); AddRow(moreLayout, ribbonList);
        AddRow(moreLayout, markHeading); AddRow(moreLayout, markList);
        Show(r => r.HasRibbons, ribbonHeading, ribbonList, countedRibbons);
        Show(r => Catalog.GetRibbons(r).Any(x => x.IsMark), markHeading, markList);
        foreach (var list in new[] { ribbonList, markList })
            list.ItemCheck += (_, change) => {
                if (binding || current == null || list.Items[change.Index] is not RibbonChoice ribbon) return;
                current.Edit.More.SetRibbon(ribbon.Property, change.NewValue == CheckState.Checked ? 1 : 0); UpdateState();
            };
        moreBinders.Add(e => {
            foreach (var list in new[] { ribbonList, markList })
                for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, e.Edit.More.HasRibbon(((RibbonChoice)list.Items[i]).Property));
            foreach (var (ribbon, box) in countedBoxes) box.Value = Math.Clamp(e.Edit.More.Ribbons.GetValueOrDefault(ribbon.Property), 0, ribbon.Max);
        });
    }

    // The lists belong to the game, so they are made once for each save.
    void ShowRibbonChoices(GameRules rules)
    {
        var ribbons = Catalog.GetRibbons(rules);
        ribbonList.Items.Clear(); ribbonList.Items.AddRange(ribbons.Where(r => !r.IsCounted && !r.IsMark).Cast<object>().ToArray());
        markList.Items.Clear(); markList.Items.AddRange(ribbons.Where(r => !r.IsCounted && r.IsMark).Cast<object>().ToArray());
        foreach (var control in countedRibbons.Controls.Cast<Control>().ToList()) control.Dispose();
        countedRibbons.Controls.Clear(); countedRibbons.RowStyles.Clear(); countedRibbons.RowCount = 0; countedBoxes.Clear();
        var cells = new List<Control>();
        foreach (var ribbon in ribbons.Where(r => r.IsCounted))
        {
            var box = Number(ribbon.Max); box.Anchor = AnchorStyles.Left; box.Width = SmallBoxWidth; box.Margin = new Padding(4, 6, 12, 4);
            box.ValueChanged += (_, _) => { if (!binding && current != null) { current.Edit.More.SetRibbon(ribbon.Property, (int)box.Value); UpdateState(); } };
            countedBoxes.Add((ribbon, box));
            cells.Add(FieldLabel(ribbon.Label)); cells.Add(box);
            if (cells.Count == countedRibbons.ColumnCount) { AddRow(countedRibbons, [.. cells]); cells.Clear(); }
        }
        if (cells.Count != 0) AddRow(countedRibbons, [.. cells]);
    }

    // Everything at once when there is no game to ask: the layout checks have to reach what this save doesn't show.
    void ShowMoreGroups(GameRules? rules)
    {
        foreach (var (shows, controls) in moreGroups)
            foreach (var control in controls) control.Visible = rules == null || shows(rules);
    }

    void ApplyMoreRules(GameRules rules)
    {
        ShowRibbonChoices(rules);
        ShowMoreGroups(rules);
        pokerusStrainBox.Maximum = rules.MaxPokerusStrain;
        metLevelBox.Maximum = rules.MaxMetLevel;
        moreLayout.Visible = true;
    }

    void BindMoreData(PokemonEntry e)
    {
        bool wasBinding = binding; binding = true;
        foreach (var bind in moreBinders) bind(e);
        bool egg = e.Original.IsEgg;
        moreNote.Text = egg ? T("Un huevo no guarda estos datos hasta que eclosiona.") : "";
        moreNote.Visible = egg;
        foreach (var control in moreLayout.Controls.Cast<Control>().Where(c => c != moreNote)) control.Enabled = !egg;
        binding = wasBinding;
    }

    void CheckMoreData(Action<bool, string> Check)
    {
        var rules = document!.Rules;
        tabs.SelectedIndex = MoreDataTab;
        Check(tabs.TabPages[MoreDataTab].Text == T("Más datos") && tabs.TabCount == MoreDataTab + 1, "The Pokémon editor has a tab for more data");
        Check(gamePicker.Visible && metPlacePicker.Visible && metLevelBox.Visible && metDateFields.Visible && hyperBoxes[0].Visible && alphaBox.Visible && heightBox.Visible
            && !contestBoxes[0].Visible && !teraPicker.Visible && !dynamaxBox.Visible && !gvBoxes[0].Visible && !pokerusStrainBox.Visible && !ppBoxes[0].Visible,
            "The tab shows what this game keeps and nothing else");
        var e = current!;
        int level = e.Baseline.More.MetLevel == 50 ? 51 : 50;
        metLevelBox.Value = level;
        Check(e.Dirty && save.Enabled && e.Preview().MetLevel == level && Text.Contains(T(" · {0} cambios pendientes", 1)), "Changing the met level stages it and counts as a pending change");
        metDateFields.Value = new(2024, 2, 30);
        Check(!save.Enabled && validation.Text.Contains("2024-02-30"), "An impossible date disables Save and explains why");
        metDateFields.Value = new(2024, 2, 29);
        Check(save.Enabled && e.Preview().MetDate == new DateOnly(2024, 2, 29), "A real date is accepted");
        var place = metPlacePicker.Items.Cast<Choice>().First(c => c.Id != e.Baseline.More.MetLocation);
        metPlacePicker.SelectMove(place.Id);
        Check(e.Preview().MetLocation == place.Id, "Choosing a place stages it");
        var nickname = e.Edit.Nickname;
        var language = languagePicker.Items.Cast<Choice>().First(c => c.Id != e.Baseline.More.Language);
        languagePicker.SelectedItem = language;
        Check(e.Preview().Language == language.Id && (e.Original.IsNicknamed ? e.Edit.Nickname == nickname : nicknameBox.Text.Length == 0), "Choosing a language stages it, and a name that follows the language goes with it");
        languagePicker.SelectedItem = languagePicker.Items.Cast<Choice>().First(c => c.Id == e.Baseline.More.Language);
        Check(nicknameBox.Text == nickname, "Going back to the language brings the name back");
        var ribbon = (RibbonChoice)ribbonList.Items[0];
        ribbonList.SetItemChecked(0, !e.Baseline.More.HasRibbon(ribbon.Property));
        Check(e.Edit.More.HasRibbon(ribbon.Property) != e.Baseline.More.HasRibbon(ribbon.Property) && Draft.From(e.Preview()).More.HasRibbon(ribbon.Property) == e.Edit.More.HasRibbon(ribbon.Property), "Ticking a ribbon stages it");
        Check(markList.Items.Count != 0 && markList.Visible, "This game has its own list of marks");
        var mark = (RibbonChoice)markList.Items[0];
        markList.SetItemChecked(0, !e.Baseline.More.HasRibbon(mark.Property));
        Check(Draft.From(e.Preview()).More.HasRibbon(mark.Property) == e.Edit.More.HasRibbon(mark.Property), "Ticking a mark stages it");
        var counted = countedBoxes[0];
        counted.Box.Value = 3;
        Check(e.Edit.More.Ribbons[counted.Ribbon.Property] == 3, "A counted ribbon takes a number");
        hyperBoxes[0].Checked = !hyperBoxes[0].Checked;
        Check(((IHyperTrain)e.Preview()).IsHyperTrained(0) == hyperBoxes[0].Checked, "Hyper training stages it");
        int shownHP = int.Parse(resultingStats[0].Text);
        Check(shownHP == e.Preview().GetStats(e.Preview().PersonalInfo)[0], "Hyper training shows in the resulting stats");
        heightBox.Value = heightBox.Value == 200 ? 201 : 200;
        Check(((IScaledSize)e.Preview()).HeightScalar == (int)heightBox.Value, "Changing the height stages it");
        alphaBox.Checked = !alphaBox.Checked;
        Check(((IAlpha)e.Preview()).IsAlpha == alphaBox.Checked, "The alpha flag stages it");
        var relearn = relearnPickers[0].Items.Cast<Choice>().First(c => c.Id != 0 && c.Id != e.Baseline.More.Relearn[0]);
        relearnPickers[0].SelectMove(relearn.Id);
        Check(e.Preview().RelearnMove1 == relearn.Id, "Choosing a relearn move stages it");
        markingButtons[0].PerformClick();
        Check(e.Edit.More.Markings[0] != e.Baseline.More.Markings[0] && Draft.From(e.Preview()).More.Markings[0] == e.Edit.More.Markings[0], "Pressing a marking stages it");
        tabs.SelectedIndex = DetailsTab;
        var illegal = Catalog.CheckLegality(e);
        Check(!illegal.Valid && legalityReport.Text == illegal.Report.ReplaceLineEndings("\r\n"), "The legality on the details tab follows these changes");
        tabs.SelectedIndex = MoreDataTab;
        restoreSelected.PerformClick();
        Check(document.DirtyCount == 0 && !save.Enabled && (int)metLevelBox.Value == e.Baseline.More.MetLevel && metDateFields.Value == e.Baseline.More.MetDate
            && e.Edit.SameAs(e.Baseline) && ribbonList.GetItemChecked(0) == e.Baseline.More.HasRibbon(ribbon.Property), "Undo restores everything on the tab");
        var others = document.Pokemon.First(p => p != e);
        Pick(others);
        Check(gamePicker.SelectedItem is Choice shown && shown.Id == others.Edit.More.Game && !document.HasChanges, "Selecting another Pokémon shows its data and stages nothing");
        Pick(e);
        tabs.SelectedIndex = 0;
    }

    // Reaches every row of every game, and fills them with the longest names this language has.
    void CheckMoreLayout(Action<bool, string> Check, string sizeLabel)
    {
        var rules = document!.Rules;
        tabs.SelectedIndex = MoreDataTab;
        ShowMoreGroups(null);
        gigantamaxBox.Visible = true;
        string longest = Catalog.Names.movelist.MaxBy(n => n.Length)!;
        foreach (var label in ppLabels) label.Text = longest;
        metPlaceLabel.Text = eggPlaceLabel.Text; eggPlaceLabel.Visible = eggPlacePicker.Visible = eggDateLabel.Visible = eggDateFields.Visible = true;
        foreach (var button in markingButtons) button.Visible = true;
        moreNote.Text = T("Un huevo no guarda estos datos hasta que eclosiona."); moreNote.Visible = true;
        foreach (var text in new[] { T("Lugar de encuentro"), T("Lugar de eclosión"), T("Fecha de encuentro"), T("Fecha de eclosión") })
        {
            metPlaceLabel.Text = text; metDateLabel.Text = text;
            PerformLayout(); Application.DoEvents();
            var clipped = Clipped(tabs.TabPages[MoreDataTab]);
            Check(clipped.Length == 0, $"Every text of the more data tab fits ({sizeLabel}) {clipped}");
        }
        var page = tabs.TabPages[MoreDataTab];
        var cut = Descendants(page).FirstOrDefault(c => c.Visible && page.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)).Right > page.ClientSize.Width);
        Check(cut == null, $"Nothing is cut off on the right of the more data tab ({sizeLabel}; {cut?.GetType().Name} {cut?.Text} {cut?.Bounds} of {cut?.Parent?.ClientSize}, page {page.ClientSize})");
        var squeezed = Descendants(page).FirstOrDefault(c => c.Visible && c.Parent is TableLayoutPanel && c.Bottom > c.Parent.ClientSize.Height);
        Check(squeezed == null, $"Every row of the more data tab shows whole ({sizeLabel}; {squeezed?.GetType().Name} {squeezed?.Text} {squeezed?.Bounds} of {squeezed?.Parent?.ClientSize})");
        var boxes = Descendants(page).OfType<NumericUpDown>().Where(b => b.Visible).ToList();
        Check(boxes.All(b => b.Width >= TextRenderer.MeasureText(b.Maximum.ToString(), b.Font).Width), $"Each number fits its field ({sizeLabel})");
        Check(tabs.GetTabRect(tabs.TabCount - 1).Right <= tabs.ClientSize.Width, $"All the tabs fit on one line ({sizeLabel})");
        ShowMoreGroups(rules);
        if (current != null) BindMoreData(current);
        Check(moreGroups.All(g => g.Shows(rules) || g.Controls.All(c => !c.Visible)), $"The tab shows nothing this game does not keep ({sizeLabel})");
        tabs.SelectedIndex = 0;
    }
}
