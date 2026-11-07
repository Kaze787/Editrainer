using PKHeX.Core;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>One change for many Pokémon. It stays pending, Pokémon by Pokémon, until the game is saved.</summary>
sealed class BatchDialog : Form
{
    const int ScrollBarRoom = 24, ResultsWidth = 500, SmallestResults = 400, LowestContent = 430, StatusHeight = 40, ActionsHeight = 48, StatCount = 6, NatureCount = 25;
    readonly SaveDocument document;
    readonly GameRules rules;
    readonly int box;
    readonly TableLayoutPanel choices = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = Padding.Empty };
    readonly Panel scroller = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
    readonly ComboBox scope = Theme.Combo(), nature = Theme.Combo(), ball = Theme.Combo();
    readonly CheckBox ivs = Tick(), evs = Tick(), level = Tick(), natures = Tick(), shiny = Tick(), friendship = Tick(), balls = Tick(), item = Tick(), nickname = Tick(), trainer = Tick();
    readonly RadioButton ivMaximum = Option(true), ivExact = Option(false), evZero = Option(true), evSpread = Option(false), shinyYes = Option(true), shinyNo = Option(false);
    readonly NumericUpDown ivValue = Number(), levelValue = Number();
    readonly NumericUpDown[] evValues = new NumericUpDown[StatCount];
    readonly TableLayoutPanel spread = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = StatCount, RowCount = 2, Margin = new Padding(26, 0, 0, 6), Anchor = AnchorStyles.Left };
    readonly Label evTotal = Theme.Label("", 9, true, Theme.Accent);
    readonly TextBox trainerName = new() { Dock = DockStyle.Fill, Font = Theme.Font(10), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 6, 4, 4) };
    readonly Label summary = Theme.Label("", 9, true);
    readonly DataGridView skipped = new() { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, AutoGenerateColumns = false,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells,
        BackgroundColor = Theme.Surface, BorderStyle = Theme.IsDark ? BorderStyle.None : BorderStyle.FixedSingle, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Theme.Line, EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        ColumnHeadersHeight = 30, RowTemplate = { Height = 28 }, Margin = new Padding(3, 4, 3, 6) };
    readonly Label status = Theme.Label("", 9, color: Theme.Muted);
    readonly Button apply = Theme.Button(T("Aplicar"), true), undo = Theme.Button(T("Deshacer el último lote")), close = Theme.Button(T("Cerrar"));

    /// <summary>What was done here last, for the editor to repeat. Null when nothing was.</summary>
    internal string? Outcome { get; private set; }

    public BatchDialog(SaveDocument document, int box)
    {
        this.document = document; this.box = box; rules = document.Rules;
        Text = T("Aplicar a varios"); Font = Theme.Font(); BackColor = Theme.Background; ForeColor = Theme.Ink;
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = MaximizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
        BuildChoices();
        scroller.Controls.Add(choices);

        var results = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(12, 0, 0, 0) };
        results.RowStyles.Add(new(SizeType.Absolute, 34)); results.RowStyles.Add(new(SizeType.Absolute, 66)); results.RowStyles.Add(new(SizeType.Percent, 100));
        results.Controls.Add(Theme.Label(T("Resultado"), 10, true), 0, 0);
        results.Controls.Add(summary, 0, 1);
        BuildResults();
        results.Controls.Add(skipped, 0, 2);

        int left = choices.GetPreferredSize(Size.Empty).Width + ScrollBarRoom;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(16, 12, 16, 8) };
        root.ColumnStyles.Add(new(SizeType.Absolute, left)); root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, StatusHeight)); root.RowStyles.Add(new(SizeType.Absolute, ActionsHeight));
        root.Controls.Add(scroller, 0, 0); root.Controls.Add(results, 1, 0);
        root.Controls.Add(status, 0, 1); root.SetColumnSpan(status, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        close.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(close); actions.Controls.Add(apply); actions.Controls.Add(undo);
        root.Controls.Add(actions, 0, 2); root.SetColumnSpan(actions, 2);
        Controls.Add(root); CancelButton = close;

        apply.Click += (_, _) => Apply();
        undo.Click += (_, _) => UndoLast();
        apply.EnabledChanged += (_, _) => apply.BackColor = apply.Enabled ? Theme.Accent : Theme.Line;
        int high = Math.Max(choices.GetPreferredSize(Size.Empty).Height, LowestContent) + StatusHeight + ActionsHeight + root.Padding.Vertical;
        ClientSize = new Size(left + ResultsWidth + root.Padding.Horizontal, high);
        MinimumSize = new Size(Width - ResultsWidth + SmallestResults, Height);
        summary.Text = T("Elige a quién y qué cambiar. Lo que un Pokémon no admita se omite y se explica aquí.");
        ActiveControl = ivs;   // the first control would otherwise open with its text selected, as if something had been picked
        ShowState();
    }

    static CheckBox Tick() => new() { AutoSize = true, Font = Theme.Font(10, true), Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 14, 7) };
    static RadioButton Option(bool chosen) => new() { AutoSize = true, Font = Theme.Font(10), Checked = chosen, Margin = new Padding(3, 7, 10, 7) };
    static NumericUpDown Number() => new() { Minimum = 0, Font = Theme.Font(10), TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle, Width = 72, Margin = new Padding(3, 4, 6, 4) };
    static FlowLayoutPanel Line(params Control[] controls)
    {
        var line = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
        line.Controls.AddRange(controls);
        return line;
    }

    int NewRow()
    {
        choices.RowStyles.Add(new(SizeType.AutoSize));
        return choices.RowCount++;
    }

    // A change with nothing to choose takes the whole line. One that has something waits for its box to be ticked.
    void Row(CheckBox box, string title, Control? value = null)
    {
        int row = NewRow();
        box.Text = title; box.CheckedChanged += (_, _) => ShowState();
        choices.Controls.Add(box, 0, row);
        if (value == null) { choices.SetColumnSpan(box, 2); return; }
        choices.Controls.Add(value, 1, row);
        value.Enabled = false;
        box.CheckedChanged += (_, _) => value.Enabled = box.Checked;
    }

    void BuildChoices()
    {
        choices.ColumnStyles.Add(new(SizeType.AutoSize)); choices.ColumnStyles.Add(new(SizeType.Percent, 100));
        void AddScope(BatchScope kind, string name) => scope.Items.Add(new Choice((int)kind, T("{0} · {1} Pokémon", name, document.InScope(kind, box).Count)));
        if (document.Save.HasParty) AddScope(BatchScope.Team, T("Equipo"));
        if (document.BoxCount != 0)
        {
            AddScope(BatchScope.OpenBox, T("Caja abierta: {0}", rules.BoxName(box)));
            AddScope(BatchScope.AllBoxes, T("Todas las cajas"));
        }
        AddScope(BatchScope.Everyone, T("Todos"));
        scope.SelectedIndex = 0; scope.SelectedIndexChanged += (_, _) => ShowState();
        var scopeTitle = Theme.Label(T("Alcance"), 10, true); scopeTitle.AutoSize = true; scopeTitle.Margin = new Padding(3, 10, 14, 8);
        int scopeRow = NewRow();
        choices.Controls.Add(scopeTitle, 0, scopeRow); choices.Controls.Add(scope, 1, scopeRow);

        ivMaximum.Text = $"{T("Máximo")} ({rules.MaxIV})"; ivExact.Text = T("Valor");
        ivValue.Maximum = rules.MaxIV; ivValue.Value = rules.MaxIV; ivValue.Enabled = false;
        ivExact.CheckedChanged += (_, _) => ShowState(); ivValue.ValueChanged += (_, _) => ShowState();
        Row(ivs, rules.IVName, Line(ivMaximum, ivExact, ivValue));

        evZero.Text = T("A cero"); evSpread.Text = T("Reparto");
        evTotal.AutoSize = true; evTotal.Dock = DockStyle.None; evTotal.Margin = new Padding(3, 9, 3, 7);
        evSpread.CheckedChanged += (_, _) => ShowState();
        Row(evs, rules.EVName, Line(evZero, evSpread, evTotal));
        BuildSpread();
        int spreadRow = NewRow();
        choices.Controls.Add(spread, 0, spreadRow); choices.SetColumnSpan(spread, 2);

        levelValue.Minimum = 1; levelValue.Maximum = Experience.MaxLevel; levelValue.Value = Experience.MaxLevel; levelValue.Anchor = AnchorStyles.Left;
        Row(level, T("Nivel"), levelValue);
        if (rules.HasNature)
        {
            nature.Items.AddRange(Enumerable.Range(0, NatureCount).Select(i => (object)new Choice(i, Catalog.Names.natures[i])).ToArray());
            nature.SelectedIndex = 0; nature.DropDownWidth = 240;
            Row(natures, T("Naturaleza"), nature);
        }
        if (rules.HasShiny)
        {
            shinyYes.Text = T("Sí"); shinyNo.Text = T("No");
            Row(shiny, T("Shiny"), Line(shinyYes, shinyNo));
        }
        if (rules.HasFriendship) Row(friendship, T("Amistad al máximo"));
        if (rules.HasBall)
        {
            var list = Catalog.GetBalls(rules);
            ball.Items.AddRange(list.Cast<object>().ToArray());
            ball.SelectedIndex = Math.Max(0, list.FindIndex(b => b.Id == (int)Ball.Poke)); ball.DropDownWidth = 240;
            Row(balls, T("Poké Ball"), ball);
        }
        if (rules.HasHeldItems) Row(item, T("Quitar el objeto equipado (vuelve a la bolsa)"));
        Row(nickname, T("Vaciar el apodo (recupera el nombre de la especie)"));
        trainerName.MaxLength = document.Save.MaxStringLengthTrainer; trainerName.Text = document.Save.OT;
        trainerName.TextChanged += (_, _) => ShowState();
        Row(trainer, T("Entrenador original"), trainerName);
    }

    void BuildSpread()
    {
        for (int stat = 0; stat < StatCount; stat++)
        {
            spread.ColumnStyles.Add(new(SizeType.AutoSize));
            var field = evValues[stat] = Number();
            field.Maximum = rules.MaxEV; field.ValueChanged += (_, _) => ShowState();
            if (!rules.IsStatEditable(stat, false)) continue;
            var title = Theme.Label(rules.StatName(stat), 8, color: Theme.Muted); title.AutoSize = true; title.Dock = DockStyle.None; title.Margin = new Padding(3, 0, 6, 0);
            spread.Controls.Add(title, stat, 0); spread.Controls.Add(field, stat, 1);
        }
        spread.Enabled = false;
    }

    void BuildResults()
    {
        skipped.DefaultCellStyle.Font = Theme.Font(9); skipped.DefaultCellStyle.ForeColor = Theme.Ink; skipped.DefaultCellStyle.BackColor = Theme.Surface;
        skipped.DefaultCellStyle.SelectionBackColor = Theme.AccentLight; skipped.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        skipped.ColumnHeadersDefaultCellStyle.Font = Theme.Font(9, true); skipped.ColumnHeadersDefaultCellStyle.BackColor = Theme.Background;
        skipped.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted; skipped.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Background;
        skipped.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Muted;
        // The reason is what this list is for, so it gets all the room the name leaves.
        skipped.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("Pokémon"), AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, MinimumWidth = 40 });
        skipped.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("Motivo"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 150, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } });
    }

    List<PokemonEntry> Reached() => document.InScope((BatchScope)((Choice)scope.SelectedItem!).Id, box);

    BatchPlan Plan() => new() {
        IVs = ivs.Checked ? ivExact.Checked ? (int)ivValue.Value : rules.MaxIV : null,
        EVs = evs.Checked ? evSpread.Checked ? evValues.Select(f => (int)f.Value).ToArray() : new int[StatCount] : null,
        Level = level.Checked ? (int)levelValue.Value : null,
        Nature = natures.Checked ? (Nature)((Choice)nature.SelectedItem!).Id : null,
        Shiny = shiny.Checked ? shinyYes.Checked : null,
        MaxFriendship = friendship.Checked,
        Ball = balls.Checked ? ((Choice)ball.SelectedItem!).Id : null,
        RemoveItem = item.Checked,
        ClearNickname = nickname.Checked,
        Trainer = trainer.Checked ? trainerName.Text : null,
    };

    void Say(string text, bool problem = false) { status.Text = text; status.ForeColor = problem ? Theme.Danger : Theme.Muted; }

    void ShowState()
    {
        ivValue.Enabled = ivExact.Checked; spread.Enabled = evs.Checked && evSpread.Checked;
        int total = evValues.Sum(f => (int)f.Value);
        evTotal.Visible = evSpread.Checked;
        evTotal.Text = rules.MaxEVTotal == 0 ? T("{0}: sin límite total", rules.EVName) : T("Suma {0} de {1}", total, rules.MaxEVTotal);
        evTotal.ForeColor = rules.MaxEVTotal != 0 && total > rules.MaxEVTotal ? Theme.Danger : Theme.Accent;
        var plan = Plan();
        string? problem = plan.Problem(rules);
        bool anyone = Reached().Count != 0;
        apply.Enabled = anyone && !plan.IsEmpty && problem == null;
        undo.Enabled = document.CanUndoBatch;
        if (problem != null) Say(problem, true);
        else Say(!anyone ? T("Ahí no hay ningún Pokémon.") : plan.IsEmpty ? T("Marca al menos un cambio.") : T("Nada se escribe hasta que pulses «Guardar partida»."));
    }

    void ShowResult(string text, IEnumerable<BatchSkip> rows)
    {
        summary.Text = Outcome = text;
        skipped.Rows.Clear();
        foreach (var row in rows) skipped.Rows.Add($"{row.Entry.DisplayName} · {row.Entry.Location}", T("{0}: {1}", row.Change, row.Reason));
        skipped.ClearSelection();
    }

    void Apply()
    {
        try
        {
            UseWaitCursor = true;
            var result = document.ApplyBatch(Reached(), Plan());
            ShowResult(result.Summary, result.Skipped);
            ShowState();
        }
        // Nothing here may reach the message loop, which would close the app with unsaved edits in it.
        catch (Exception ex) { Say(ex.Message, true); }
        finally { UseWaitCursor = false; }
    }

    void UndoLast()
    {
        try { ShowResult(document.UndoBatch(), []); ShowState(); }
        catch (Exception ex) { Say(ex.Message, true); }
    }

    /// <summary>What a user does here, for the automatic checks, on any save with someone on the team and someone boxed.</summary>
    internal void RunInteractionChecks(Action<bool, string> check)
    {
        void Pick(BatchScope kind)
        {
            scope.SelectedItem = scope.Items.Cast<Choice>().First(c => c.Id == (int)kind);
            Application.DoEvents();
        }
        var team = document.InScope(BatchScope.Team, box);
        var other = document.InScope(BatchScope.AllBoxes, box)[0];
        check(!document.HasChanges && !apply.Enabled && !undo.Enabled && status.Text == T("Marca al menos un cambio."), "The batch window opens with nothing prepared and says what it needs");
        check(scope.Items.Count == 4 && ((Choice)scope.Items[0]!).Label.Contains(team.Count.ToString()), "The batch window offers four scopes and counts who is in each");
        check(!ivValue.Enabled && !spread.Enabled && !levelValue.Enabled && !trainerName.Enabled, "A change waits for its box to be ticked");

        ivs.Checked = true;
        check(apply.Enabled && !ivValue.Enabled, "Ticking a change enables Apply, at the maximum by default");
        ivExact.Checked = true; ivValue.Value = 7;
        check(ivValue.Enabled && Plan().IVs == 7, "An exact value can be chosen");
        Pick(BatchScope.Team); apply.PerformClick();
        check(team.All(p => p.Edit.IVs.All(v => v == 7)) && document.DirtyCount == team.Count && !other.Dirty, "Apply prepares the change on the chosen scope and nowhere else");
        check(document.Save.BoxData.All(p => p.Species == 0 || p.IV_HP != 7 || p.IV_ATK != 7) && summary.Text.Contains($"{team.Count}") && summary.Text.Contains(T("Guarda para aplicarlo.")) && skipped.Rows.Count == 0, "The summary counts them, and the open game doesn't change");
        check(undo.Enabled, "A batch can be undone");

        other.Edit.Friendship = other.Baseline.Friendship == 200 ? 201 : 200;
        team[0].Edit.Nature = team[0].Baseline.Nature == Nature.Bold ? Nature.Timid : Nature.Bold;
        undo.PerformClick();
        check(other.Dirty && team.Skip(1).All(p => !p.Dirty) && !undo.Enabled, "Undo takes the batch back and leaves a later edit of someone else alone");
        check(team[0].Dirty && team[0].Edit.IVs.SequenceEqual(team[0].Baseline.IVs) && team[0].Edit.Nature != team[0].Baseline.Nature, "A later edit of the same Pokémon survives too");
        document.ResetPokemon(other); document.ResetPokemon(team[0]);
        check(!document.HasChanges, "Nothing is left of those edits");

        ivs.Checked = false; evs.Checked = true; evSpread.Checked = true;
        evValues[0].Value = evValues[1].Value = evValues[2].Value = rules.MaxEV;
        check(!apply.Enabled && status.ForeColor == Theme.Danger && status.Text.Length != 0 && evTotal.ForeColor == Theme.Danger, "A spread over the limit explains itself and can't be applied");
        evValues[2].Value = 0;
        check(apply.Enabled && status.ForeColor == Theme.Muted, "A spread within the limit can be applied");
        evs.Checked = false;
        trainer.Checked = true; trainerName.Text = " ";
        check(!apply.Enabled && status.ForeColor == Theme.Danger, "A blank trainer name can't be applied");
        trainer.Checked = false;

        team[0].Edit.EVs = Enumerable.Repeat(rules.MaxEV, StatCount).ToArray();
        level.Checked = true; levelValue.Value = team[0].Edit.Level == 50 ? 51 : 50;
        apply.PerformClick();
        check(skipped.Rows.Count == 1 && ((string)skipped.Rows[0].Cells[0].Value!).EndsWith(team[0].Location) && ((string)skipped.Rows[0].Cells[1].Value!).StartsWith(T("Nivel")), "What a Pokémon can't take is listed with the change and its reason");
        check(summary.Text.Contains($"{team.Count - 1} ") && team[0].Edit.Level == team[0].Baseline.Level && team[1].Edit.Level == levelValue.Value, "The rest of the batch goes through");
        team[0].Edit = team[0].Baseline.Copy();
        undo.PerformClick();
        level.Checked = false;
        check(!document.HasChanges && skipped.Rows.Count == 0 && !apply.Enabled, "Undoing that one leaves nothing pending");
    }

    /// <summary>Everything open and every result at its longest, for the checks that look at the layout.</summary>
    internal void ShowAllForChecks()
    {
        foreach (var tick in choices.Controls.OfType<CheckBox>()) tick.Checked = true;
        ivExact.Checked = true; evSpread.Checked = true;
        var skips = document.Pokemon.Select(p => new BatchSkip(p, T("Entrenador original"), T("En Game Boy sus DV deciden su aspecto, y este cambio lo alteraría."))).ToList();
        ShowResult(new BatchResult(9999, 9999, skips).Summary, skips);
        ShowState();
    }

    internal bool NeedsScrolling => scroller.VerticalScroll.Visible || scroller.HorizontalScroll.Visible;
    internal bool ButtonsFit => apply.Parent!.Controls.Cast<Control>().All(c => c.Left >= 0);
    internal bool ResultsFit => skipped.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) <= skipped.ClientSize.Width;

    /// <summary>Every sentence the status line can show, one at a time, as the checks need to measure them.</summary>
    internal IEnumerable<string> LongestMessages()
    {
        var plan = new BatchPlan { EVs = Enumerable.Repeat(rules.MaxEV, StatCount).ToArray() };
        yield return plan.Problem(rules) ?? "";
        yield return T("El nivel debe estar entre 1 y {0}.", Experience.MaxLevel);
        yield return T("El nombre del entrenador no puede quedar vacío.");
        yield return T("Nada se escribe hasta que pulses «Guardar partida».");
    }

    internal bool Fits(string message)
    {
        var size = TextRenderer.MeasureText(message, status.Font, new Size(status.ClientSize.Width, int.MaxValue), TextFormatFlags.WordBreak);
        return size.Height <= status.ClientSize.Height + 1 && size.Width <= status.ClientSize.Width + 1;
    }

    internal const int ChecksValue = 7;

    internal void PressApplyToEveryone()
    {
        scope.SelectedItem = scope.Items.Cast<Choice>().First(c => c.Id == (int)BatchScope.Everyone);
        ivs.Checked = true; ivExact.Checked = true; ivValue.Value = ChecksValue; apply.PerformClick();
    }
}
