using static Editrainer.Strings;

namespace Editrainer;

/// <summary>A tool that makes Pokémon in the background, so the editor keeps answering, and that can be told to stop.</summary>
abstract class GeneratorWindow : ToolWindow
{
    const int StatusHeight = 64;   // what they say can run to three lines in the longer languages
    readonly TableLayoutPanel above = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
    readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 12, Margin = new Padding(3, 2, 3, 6), Visible = false };
    readonly Button stop = Theme.Button(T("Detener"));
    CancellationTokenSource? running;

    protected readonly SaveDocument Document;
    protected readonly Func<int> OpenBox;

    protected GeneratorWindow(string title, Size size, Size smallest, SaveDocument document, Func<int> openBox) : base(title, size, smallest)
    {
        Document = document; OpenBox = openBox;
        MakeRoomForStatus(StatusHeight);
        above.ColumnStyles.Add(new(SizeType.Percent, 100));
        above.Controls.Add(progress, 0, 1);
        AddAbove(above);
        stop.Enabled = false; stop.Click += (_, _) => running?.Cancel();
        AddAction(stop);
        FormClosed += (_, _) => running?.Cancel();
    }

    protected void AddInput(Control control) => above.Controls.Add(control, 0, 0);

    /// <summary>The buttons that start something rest while something is running.</summary>
    protected abstract void ShowBusy(bool busy);

    /// <summary>
    /// Runs a job on another thread. Null means there is nothing to show: it was stopped, it failed and has said why,
    /// or the window has gone. With no steps the bar only shows that it is working.
    /// </summary>
    protected async Task<TResult?> Work<TResult>(int steps, Func<int, string> saying, Func<IProgress<int>, CancellationToken, TResult> job) where TResult : class
    {
        if (running != null) return null;
        using var cancel = running = new CancellationTokenSource();
        Working = true; stop.Enabled = true; ShowBusy(true);
        progress.Style = steps == 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        progress.Maximum = Math.Max(1, steps); progress.Value = 0; progress.Visible = true;
        Say(saying(0));
        var done = new Progress<int>(count => {
            if (running == null) return;
            progress.Value = Math.Min(count, progress.Maximum); Say(saying(count));
        });
        try
        {
            var result = await Task.Run(() => job(done, cancel.Token), CancellationToken.None);
            return IsDisposed ? null : result;
        }
        catch (OperationCanceledException) { if (!IsDisposed) Say(T("Generación detenida.")); return null; }
        // Nothing here may reach the message loop, which would close the app with unsaved edits in it.
        catch (Exception e) { if (!IsDisposed) Say(e.Message, true); return null; }
        finally
        {
            running = null; Working = false;
            if (!IsDisposed) { progress.Visible = false; stop.Enabled = false; ShowBusy(false); }
        }
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void PressStop() => stop.PerformClick();
}

/// <summary>Turns a Showdown set into a legal Pokémon in a free place of the boxes.</summary>
sealed class ShowdownWindow : GeneratorWindow
{
    readonly TextBox set = new() { Dock = DockStyle.Top, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Height = 150, Font = Theme.Font(10),
        BackColor = Theme.Surface, ForeColor = Theme.Ink, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 2, 3, 6), PlaceholderText = T("Pega aquí un set de Showdown…") };
    readonly Button generate = Theme.Button(T("Generar"), true), paste = Theme.Button(T("Pegar del portapapeles"));
    readonly List<(object Tag, object[] Cells)> rows = [];
    readonly Action<Arrival> arrived;
    readonly Action<PokemonEntry> showInEditor;

    public ShowdownWindow(SaveDocument document, Func<int> openBox, Action<Arrival> arrived, Action<PokemonEntry> showInEditor)
        : base(T("Generar desde Showdown"), new Size(760, 580), new Size(640, 500), document, openBox)
    {
        this.arrived = arrived; this.showInEditor = showInEditor;
        AddInput(set);
        set.TextChanged += (_, _) => KeepLines();
        SetColumns([T("Lugar"), T("Pokémon"), T("Resultado")], true);
        generate.Click += async (_, _) => await Generate();
        paste.Click += (_, _) => Paste();
        AddAction(generate); AddAction(paste);
        Grid.CellClick += (_, e) => { if (e.RowIndex >= 0) ShowRow(e.RowIndex); };
    }

    protected override void ShowBusy(bool busy) => generate.Enabled = paste.Enabled = !busy;

    // Showdown copies with bare line feeds, which this control would draw as one long line.
    void KeepLines()
    {
        string text = set.Text.ReplaceLineEndings("\r\n");
        if (text == set.Text) return;
        set.Text = text; set.SelectionStart = text.Length;
    }

    void Paste()
    {
        try { if (Clipboard.ContainsText()) set.Text = Clipboard.GetText(); }
        catch (System.Runtime.InteropServices.ExternalException e) { Say(e.Message, true); }
    }

    async Task Generate()
    {
        string text = set.Text;
        if (text.Trim().Length == 0) { Say(T("Pega primero un set de Showdown."), true); return; }
        var generator = new LegalGenerator(Document);
        var made = await Work(0, _ => T("Buscando un encuentro legal…"), (_, cancel) => generator.FromSet(text, cancel));
        if (made == null) return;
        if (made.Pokemon == null)
        {
            AddRow("", "—", generator.NameOf(made), made.Problem!);
            Say($"{generator.NameOf(made)}: {made.Problem}", true);
            return;
        }
        try
        {
            var place = Document.FreePlace(OpenBox()) ?? throw new InvalidOperationException(T("Las cajas están llenas."));
            var arrival = Document.PlaceGenerated(made.Pokemon, place);
            arrived(arrival);
            AddRow(arrival.Entry, arrival.Entry.Location, arrival.Entry.DisplayName, $"{T("Legal")} · {T("Nv. {0}", made.Pokemon.CurrentLevel)}");
            Say(arrival.Message);
        }
        catch (InvalidOperationException e) { Say(e.Message, true); }
    }

    void AddRow(object tag, params object[] cells)
    {
        rows.Insert(0, (tag, cells));
        Fill(rows);
    }

    void ShowRow(int row)
    {
        if (Grid.Rows[row].Tag is PokemonEntry entry && Document.Pokemon.Contains(entry)) showInEditor(entry);
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void PressGenerate(string text) { set.Text = text; generate.PerformClick(); }
    internal void PressRow(int row) { SelectRow(row); ShowRow(row); }
    internal string SetText => set.Text;
}

/// <summary>One legal Pokémon of each species the save lacks. Nothing reaches the boxes until it is asked to.</summary>
sealed class LivingDexWindow : GeneratorWindow
{
    readonly Button generate = Theme.Button(T("Generar"), true), place = Theme.Button(T("Colocar en las cajas"));
    readonly Action<string> placed;
    DexRun? run;

    public LivingDexWindow(SaveDocument document, Func<int> openBox, Action<string> placed)
        : base(T("Pokédex viviente"), new Size(760, 560), new Size(640, 440), document, openBox)
    {
        this.placed = placed;
        SetColumns([T("N.º"), T("Especie"), T("Resultado")], true);
        generate.Click += async (_, _) => await Generate();
        place.Click += (_, _) => Place();
        place.Enabled = false;
        AddAction(place); AddAction(generate);
        Say(T("Busca un Pokémon legal de cada especie que te falte, en su forma base y a nivel 100. Nada cambia hasta que lo coloques."));
    }

    protected override void ShowBusy(bool busy)
    {
        generate.Enabled = !busy;
        place.Enabled = !busy && run != null;
    }

    async Task Generate()
    {
        var generator = new LegalGenerator(Document);
        var owned = Document.OwnedSpecies();
        int room = Document.FreePlaces, wanted = generator.DexSpecies.Count(s => !owned.Contains(s));
        if (wanted == 0) { Say(T("Ya tienes todas las especies de este juego.")); return; }
        if (room == 0) { Say(T("Las cajas están llenas."), true); return; }
        run = null;
        var result = await Work(wanted, count => T("Generando {0} de {1}…", count, wanted), (progress, cancel) => generator.LivingDex(owned, room, progress, cancel));
        if (result == null) return;
        // What was skipped goes first: among hundreds of rows, that is what someone reads this list for.
        Fill(result.Results.OrderBy(r => r.Pokemon != null).Select(r => ((object)r, new object[] { (int)r.Species, generator.NameOf(r), Verdict(r) })));
        run = result.ReadyCount == 0 ? null : result;
        place.Enabled = run != null;
        Say(Summary(result));
    }

    static string Verdict(Generated made) => made.Pokemon == null ? T("Omitido: {0}", made.Problem) : $"{T("Legal")} · {T("Nv. {0}", made.Pokemon.CurrentLevel)}";

    static string Summary(DexRun result)
    {
        var parts = new List<string> { T("Listos: {0}. Omitidos: {1}.", result.ReadyCount, result.Skipped) };
        if (result.Stopped) parts.Insert(0, T("Generación detenida."));
        else if (result.Untried != 0) parts.Add(T("Sin sitio en las cajas: {0}.", result.Untried));
        if (result.Owned != 0) parts.Add(T("Ya en la partida: {0}.", result.Owned));
        return string.Join(" ", parts);
    }

    void Place()
    {
        if (run == null) return;
        try
        {
            int count = Document.PlaceAll(run.Ready, OpenBox());
            run = null; place.Enabled = false;
            string message = T("Pokémon colocados: {0}. Guarda para aplicarlos, o quítalos todos con «Deshacer el último cambio de cajas».", count);
            placed(message);
            Say(message);
        }
        catch (InvalidOperationException e) { Say(e.Message, true); }
    }

    /// <summary>What a user does here, for the automatic checks.</summary>
    internal void PressGenerate() => generate.PerformClick();
    internal void PressPlace() => place.PerformClick();
    internal bool CanPlace => place.Enabled;
    internal int Ready => run?.ReadyCount ?? 0;
}
