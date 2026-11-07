using static Editrainer.Strings;

namespace Editrainer;

/// <summary>A number that keeps the zeros a game shows, as in the ID 00042.</summary>
sealed class PaddedNumber : NumericUpDown
{
    public string Pattern = "D5";
    protected override void UpdateEditText()
    {
        if (UserEdit) ParseEditText();
        ChangingText = true;
        Text = ((long)Value).ToString(Pattern);
    }
}

/// <summary>The trainer of the open save. What changes here stays pending until the game is saved.</summary>
sealed class TrainerDialog : Form
{
    const int GymsPerRow = 8, WindowWidth = 680;
    readonly TrainerEditor editor;
    readonly TextBox name = new() { Dock = DockStyle.Fill, Font = Theme.Font(10), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 6, 4, 4) };
    readonly ComboBox gender = Theme.Combo();
    readonly PaddedNumber tid = Id(), sid = Id();
    readonly NumericUpDown hours = Number(), minutes = Number(), seconds = Number(), badgeCount = Number();
    readonly List<CheckBox> badges = [];
    readonly Label problem = Theme.Label("", 9, color: Theme.Danger);
    readonly Button undo = Theme.Button(T("Deshacer entrenador"));
    bool binding;

    public TrainerDialog(SaveDocument document)
    {
        editor = document.Trainer;
        Text = T("Entrenador"); Font = Theme.Font(); BackColor = Theme.Background; ForeColor = Theme.Ink;
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(WindowWidth, 400);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 14, 18, 10) };
        layout.ColumnStyles.Add(new(SizeType.AutoSize)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(layout);

        Row(layout, T("Nombre"), name);
        name.MaxLength = editor.MaxNameLength; name.TextChanged += (_, _) => Edit(d => d with { Name = name.Text });
        if (editor.HasGender)
        {
            gender.Items.AddRange([T("Masculino"), T("Femenino")]);
            gender.SelectedIndexChanged += (_, _) => Edit(d => d with { Gender = (byte)gender.SelectedIndex });
            Row(layout, T("Género"), gender);
        }
        tid.Maximum = editor.MaxTid; tid.Pattern = editor.TidFormat; tid.ValueChanged += (_, _) => Edit(d => d with { Tid = (uint)tid.Value });
        Row(layout, T("ID de entrenador"), tid);
        if (editor.HasSecretId)
        {
            sid.Maximum = editor.MaxSid; sid.Pattern = editor.SidFormat; sid.ValueChanged += (_, _) => Edit(d => d with { Sid = (uint)sid.Value });
            Row(layout, T("ID secreto"), sid);
        }
        hours.Maximum = Math.Max(editor.MaxHours, editor.Original.Hours); minutes.Maximum = seconds.Maximum = 59;
        hours.ValueChanged += (_, _) => Edit(d => d with { Hours = (int)hours.Value });
        minutes.ValueChanged += (_, _) => Edit(d => d with { Minutes = (int)minutes.Value });
        seconds.ValueChanged += (_, _) => Edit(d => d with { Seconds = (int)seconds.Value });
        var clock = Line();
        foreach (var (field, unit) in new[] { (hours, T("Horas")), (minutes, T("Minutos")), (seconds, T("Segundos")) }) { clock.Controls.Add(field); clock.Controls.Add(Unit(unit)); }
        Row(layout, T("Tiempo de juego"), clock);
        BuildBadges(layout);

        string warning = T("Cambiar el nombre o los ID hace que el juego trate a tus Pokémon actuales como recibidos de otro entrenador.");
        if (editor.HasGender && editor.GenderLeavesClothes) warning += "\n" + T("En este juego la ropa y el modelo se guardan aparte: cambiar el género no los ajusta. Si la partida da problemas, recupera el respaldo automático.");
        var hint = Theme.Label(warning + "\n" + T("Los cambios se aplican al guardar la partida."), 9, color: Theme.Muted);
        hint.AutoSize = true; hint.MaximumSize = new Size(WindowWidth - 44, 0); hint.Margin = new Padding(3, 8, 3, 3);
        layout.RowCount++; layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(hint, 0, layout.RowCount - 1); layout.SetColumnSpan(hint, 2);

        problem.AutoEllipsis = true;
        layout.RowCount++; layout.RowStyles.Add(new(SizeType.Absolute, 24));
        layout.Controls.Add(problem, 0, layout.RowCount - 1); layout.SetColumnSpan(problem, 2);

        var close = Theme.Button(T("Cerrar"), true); close.DialogResult = DialogResult.OK;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
        buttons.Controls.Add(close); buttons.Controls.Add(undo);
        layout.RowCount++; layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(buttons, 0, layout.RowCount - 1); layout.SetColumnSpan(buttons, 2);
        AcceptButton = CancelButton = close;
        undo.Click += (_, _) => { editor.Reset(); Bind(); };
        // A number typed and left without moving away is only read when the field is checked.
        FormClosing += (_, _) => Validate();
        Bind();
        ClientSize = new Size(WindowWidth, layout.GetPreferredSize(new Size(WindowWidth, 0)).Height);
    }

    static NumericUpDown Number() => new() { Minimum = 0, Font = Theme.Font(10), TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle, Width = 76, Margin = new Padding(4, 6, 4, 4) };
    static PaddedNumber Id() => new() { Minimum = 0, Font = Theme.Font(10), TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle, Width = 130, Margin = new Padding(4, 6, 4, 4), Anchor = AnchorStyles.Left };
    static FlowLayoutPanel Line() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
    static Label Unit(string text)
    {
        var label = Theme.Label(text, 9, color: Theme.Muted); label.AutoSize = true; label.Dock = DockStyle.None; label.Margin = new Padding(0, 10, 8, 4);
        return label;
    }

    static void Row(TableLayoutPanel layout, string title, Control field)
    {
        var label = Theme.Label(title, 10, true); label.AutoSize = true; label.Margin = new Padding(3, 10, 14, 8);
        layout.RowCount++; layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(label, 0, layout.RowCount - 1); layout.Controls.Add(field, 1, layout.RowCount - 1);
    }

    void BuildBadges(TableLayoutPanel layout)
    {
        if (editor.MaxBadgeCount != 0)
        {
            badgeCount.Maximum = editor.MaxBadgeCount; badgeCount.ValueChanged += (_, _) => Edit(d => d with { Badges = (int)badgeCount.Value });
            Row(layout, T("Medallas"), badgeCount);
        }
        for (int first = 0; first < editor.BadgeFlags; first += GymsPerRow)
        {
            var line = Line();
            for (int i = first; i < first + GymsPerRow; i++)
            {
                var badge = new CheckBox { Text = (i - first + 1).ToString(), AutoSize = true, Font = Theme.Font(10), Margin = new Padding(4, 8, 6, 4) };
                int bit = 1 << i;
                badge.CheckedChanged += (_, _) => Edit(d => d with { Badges = badge.Checked ? d.Badges | bit : d.Badges & ~bit });
                badges.Add(badge); line.Controls.Add(badge);
            }
            Row(layout, editor.BadgeFlags <= GymsPerRow ? T("Medallas") : first == 0 ? T("Medallas de Johto") : T("Medallas de Kanto"), line);
        }
    }

    void Edit(Func<TrainerData, TrainerData> change)
    {
        if (binding) return;
        editor.Edit = change(editor.Edit);
        ShowState();
    }

    void Bind()
    {
        binding = true;
        var d = editor.Edit;
        name.Text = d.Name;
        if (editor.HasGender) gender.SelectedIndex = Math.Min((int)d.Gender, 1);
        tid.Value = Math.Min(d.Tid, tid.Maximum); sid.Value = Math.Min(d.Sid, editor.HasSecretId ? sid.Maximum : 0);
        hours.Value = Math.Min(d.Hours, hours.Maximum); minutes.Value = Math.Min(d.Minutes, 59); seconds.Value = Math.Min(d.Seconds, 59);
        if (editor.MaxBadgeCount != 0) badgeCount.Value = Math.Min(d.Badges, editor.MaxBadgeCount);
        for (int i = 0; i < badges.Count; i++) badges[i].Checked = (d.Badges >> i & 1) != 0;
        binding = false;
        ShowState();
    }

    void ShowState()
    {
        problem.Text = editor.Validate() ?? "";
        undo.Enabled = editor.Changed;
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void RunInteractionChecks(SaveDocument document, Action<bool, string> check)
    {
        var start = editor.Original;
        check(name.Text == start.Name && tid.Text == start.Tid.ToString(editor.TidFormat) && sid.Text == start.Sid.ToString(editor.SidFormat), "The trainer window shows the name and the IDs as the game does");
        check(hours.Value == start.Hours && minutes.Value == start.Minutes && seconds.Value == start.Seconds, "The trainer window shows the play time");
        check(!undo.Enabled && !document.HasChanges, "Opening the trainer window stages nothing");
        name.Text = "Ana"; tid.Value = 123; sid.Value = 7; hours.Value = 321; minutes.Value = 9; seconds.Value = 8; gender.SelectedIndex = 1 - gender.SelectedIndex;
        check(editor.Edit == start with { Name = "Ana", Tid = 123, Sid = 7, Hours = 321, Minutes = 9, Seconds = 8, Gender = (byte)(1 - start.Gender) } && document.HasChanges && undo.Enabled, "Trainer fields stage their changes");
        check(tid.Text == 123.ToString(editor.TidFormat) && sid.Text == 7.ToString(editor.SidFormat), "IDs keep the game's leading zeros");
        tid.Text = "42";
        check(tid.Value == 42 && tid.Text == 42.ToString(editor.TidFormat) && editor.Edit.Tid == 42, "A typed ID is read and shown with its zeros");
        tid.Value = 123;
        check(document.Save.OT == start.Name, "The open game doesn't change until it is saved");
        name.Text = "";
        check(problem.Text.Length != 0 && document.ValidateEdits() != null, "An empty trainer name explains why it can't be saved");
        name.Text = "Ana";
        check(problem.Text.Length == 0, "A valid name clears the warning");
        check(EditorView.Clipped(this).Length == 0, "Trainer window texts fit");
        undo.PerformClick();
        check(!document.HasChanges && !undo.Enabled && name.Text == start.Name && hours.Value == start.Hours && gender.SelectedIndex == start.Gender, "Undo restores the trainer");
    }
}
