using PKHeX.Core;
using System.Text.Json;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>What the two grids and the Tools menu do to where Pokémon are.</summary>
public sealed partial class EditorView
{
    readonly ContextMenuStrip slotMenu = new();

    void WireBoxes()
    {
        teamGrid.Partner = boxGrid; boxGrid.Partner = teamGrid;
        status.Margin = new Padding(3, 0, 3, 0);   // room for what a box change has to say, on two lines
        foreach (var grid in new[] { teamGrid, boxGrid })
        {
            grid.Dropped += DropSlot;
            grid.MenuRequested += (index, point) => {
                BuildSlotMenu(grid, index);
                if (slotMenu.Items.Count != 0) slotMenu.Show(grid, point);
            };
        }
    }

    void BuildGeneratorTools()
    {
        AddTool(T("Generar desde Showdown…"), () => OpenGenerator(() => new ShowdownWindow(document!, () => box, ShowArrival, ShowPokemon)));
        AddTool(T("Pokédex viviente…"), () => OpenGenerator(() => new LivingDexWindow(document!, () => box, ShowPlaced)));
    }

    void BuildBoxTools()
    {
        AddTool(T("Ordenar caja por Pokédex"), () => SortOpenBox(BoxOrder.Pokedex));
        AddTool(T("Ordenar caja por nivel"), () => SortOpenBox(BoxOrder.Level));
        AddTool(T("Ordenar caja por nombre"), () => SortOpenBox(BoxOrder.Name));
        AddTool(T("Deshacer el último cambio de cajas"), UndoBoxChange);
        AddTool(T("Deshacer todos los cambios de cajas"), UndoBoxChanges);
    }

    SlotPlace? PlaceOf(SlotGrid grid, int index) => document == null || !grid.Movable || index < 0 ? null
        : grid == teamGrid ? new SlotPlace(true, index) : new SlotPlace(false, box * document.Save.BoxSlotCount + index);

    void DropSlot(SlotGrid from, int fromIndex, SlotGrid to, int toIndex, bool copy)
    {
        if (document == null || from.Slots[fromIndex] is not { } entry || PlaceOf(to, toIndex) is not { } target || entry.Place == target) return;
        if (copy) Arrive(() => document.Clone(entry, target));
        else Arrange(() => document.Move(entry, target), entry);
    }

    void BuildSlotMenu(SlotGrid grid, int index)
    {
        slotMenu.Items.Clear();
        if (document == null || index < 0 || index >= grid.Slots.Count) return;
        void Add(string text, Action run) => slotMenu.Items.Add(text, null, (_, _) => run());
        var open = document;
        bool arranging = open.CanArrange;
        // The selection outlives turning the page, which is how a Pokémon reaches another box.
        var selected = arranging && grid.Movable ? current : null;
        if (grid.Slots[index] is { } entry)
        {
            if (selected != null && selected != entry) Add(T("Cambiar de lugar con {0}", selected.DisplayName), () => Arrange(() => open.Move(selected, entry.Place), selected));
            if (arranging) Add(T("Clonar"), () => CloneToFreePlace(entry));
            Add(T("Exportar a archivo…"), () => ExportDialog(entry));
            if (arranging) Add(T("Borrar"), () => Arrange(() => open.Delete(entry)));
        }
        else if (arranging && PlaceOf(grid, index) is { } place)
        {
            if (selected != null)
            {
                Add(T("Mover aquí a {0}", selected.DisplayName), () => Arrange(() => open.Move(selected, place), selected));
                Add(T("Clonar aquí a {0}", selected.DisplayName), () => Arrive(() => open.Clone(selected, place)));
            }
            Add(T("Importar desde archivo…"), () => ImportDialog(place));
        }
        if (!open.CanUndoLayout) return;
        if (slotMenu.Items.Count != 0) slotMenu.Items.Add(new ToolStripSeparator());
        Add(T("Deshacer el último cambio de cajas"), UndoBoxChange);
    }

    void CloneToFreePlace(PokemonEntry entry)
    {
        if (document!.FreePlace(entry.IsParty ? box : document.BoxOf(entry)) is { } place) Arrive(() => document.Clone(entry, place));
        else Warn(T("Las cajas están llenas."));
    }

    void ExportDialog(PokemonEntry entry)
    {
        using var dialog = new SaveFileDialog { Title = T("Exportar Pokémon"), Filter = T("Archivo de Pokémon") + "|*." + entry.Original.Extension,
            FileName = SaveDocument.PokemonFileName(entry), AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(Window) == DialogResult.OK) ExportTo(entry, dialog.FileName);
    }

    void ExportTo(PokemonEntry entry, string path)
    {
        try { File.WriteAllBytes(path, SaveDocument.PokemonFile(entry)); Notify(T("{0} exportado a {1}.", entry.DisplayName, Path.GetFileName(path))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Warn(ex.Message); }
    }

    void ImportDialog(SlotPlace place)
    {
        using var dialog = new OpenFileDialog { Title = T("Importar Pokémon"),
            Filter = T("Archivo de Pokémon") + "|" + string.Join(";", EntityFileExtension.GetExtensionsAll().Select(e => "*." + e)) + "|" + T("Todos los archivos") + "|*.*" };
        if (dialog.ShowDialog(Window) == DialogResult.OK) Arrive(() => document!.Import(dialog.FileName, place));
    }

    void SortOpenBox(BoxOrder order) => Arrange(() => document!.SortBox(box, order));

    void UndoBoxChange()
    {
        if (document!.CanUndoLayout) Arrange(() => { document.UndoLayout(); return T("Último cambio de cajas deshecho."); });
        else Notify(T("No hay cambios de cajas que deshacer."));
    }

    void UndoBoxChanges()
    {
        if (document!.CanUndoLayout) Arrange(() => { document.ResetLayout(); return T("Cambios de cajas deshechos."); });
        else Notify(T("No hay cambios de cajas que deshacer."));
    }

    void Arrange(Func<string> change, PokemonEntry? show = null)
    {
        try { var message = change(); ShowArranged(show); Notify(message); }
        catch (InvalidOperationException ex) { Warn(ex.Message); }
    }

    void Arrive(Func<Arrival> arrive)
    {
        try { var arrival = arrive(); ShowArranged(arrival.Entry); Notify(arrival.Message); }
        catch (InvalidOperationException ex) { Warn(ex.Message); }
    }

    void ShowArranged(PokemonEntry? show)
    {
        var open = document!;
        for (int i = 0; i < boxPicker.Items.Count; i++)
            if (open.BoxLabel(i) is var label && !label.Equals(boxPicker.Items[i])) boxPicker.Items[i] = label;
        resources.RefreshDraft();
        show ??= current != null && open.Pokemon.Contains(current) ? current : open.BoxSlots(box).OfType<PokemonEntry>().FirstOrDefault();
        if (show != null) current = show;
        RefreshList();
        if (show != null && shown.Contains(show)) Pick(show);
    }

    void Warn(string text) { status.Text = text; status.ForeColor = Theme.Danger; UpdateState(); }

    // Everything generated arrives through the boxes, so a save that can't arrange its Pokémon can't take any.
    void OpenGenerator(Func<ToolWindow> open)
    {
        if (document!.CanArrange) OpenTool(open());
        else Warn(T("Esta partida no permite reorganizar sus Pokémon."));
    }

    void ShowArrival(Arrival arrival) { ShowArranged(arrival.Entry); Notify(arrival.Message); }

    void ShowPlaced(string message) { ShowArranged(null); Notify(message); }

    // Both tools from the menu, on the save itself. Leaves the document as it found it.
    void CheckGenerators(Action<bool, string> Check)
    {
        var open = document!;
        void Tool(string text) => AllTools().First(i => i.Text == text).PerformClick();
        search.Text = ""; tabs.SelectedIndex = 0; ShowBox(0);
        int before = open.Pokemon.Count;

        var single = OpenFromMenu<ShowdownWindow>(T("Generar desde Showdown…"));
        Check(single.Owner == Window && tool == single, "Generating from a set opens beside the editor");
        single.PressGenerate("");
        Check(single.StatusText == T("Pega primero un set de Showdown.") && !single.Working, "An empty set asks for one");
        single.PressGenerate("esto no es un set");
        WaitFor(() => !single.Working, "the refused set");
        Check(single.StatusText == T("Ese texto no es un set de Showdown.") && open.Pokemon.Count == before && !open.HasChanges, "Text that isn't a set is refused and changes nothing");
        single.PressGenerate("Pikachu\nLevel: 30\nJolly Nature");
        Check(single.Working, "Generating runs in the background");
        Check(single.SetText == "Pikachu\r\nLevel: 30\r\nJolly Nature", "A set copied with bare line feeds still shows on its own lines");
        WaitFor(() => !single.Working, "the generated set");
        var made = open.Pokemon.First(p => p.IsNew);
        Check(made.Original.Species == 25 && made.Edit.Level == 30 && open.Pokemon.Count == before + 1, $"A set becomes a Pokémon in a free place ({single.StatusText})");
        Check(Catalog.CheckLegality(made).Valid && single.StatusText.Contains(made.Location), "What it generates is legal, and it says where it went");
        Check(open.HasChanges && save.Enabled && Text.Contains("1 ") && current == made, "The generated Pokémon is pending, counted and on screen");
        Check(single.RowCount == 1 && single.Cell(0, 0) == made.Location, "The window lists what it made");
        Pick(open.Pokemon[0]); single.PressRow(0);
        Check(current == made, "Clicking a row shows that Pokémon in the editor");
        tabs.SelectedIndex = 0;
        Check(open.BuildOutput().Length == open.OriginalBytes.Length, "A generated Pokémon can be written");
        single.Close();
        Tool(T("Deshacer el último cambio de cajas"));
        Check(!open.HasChanges && !save.Enabled && open.Pokemon.Count == before, "The box undo takes it back");

        var dex = OpenFromMenu<LivingDexWindow>(T("Pokédex viviente…"));
        Check(!dex.CanPlace && dex.StatusText.Length != 0, "The living Pokédex says what it does and has nothing to place yet");
        dex.PressGenerate(); dex.PressStop();
        WaitFor(() => !dex.Working, "the stopped living Pokédex");
        Check(!open.HasChanges && open.Pokemon.Count == before && dex.StatusText.Contains(T("Generación detenida.")), "Stopping leaves the save alone");
        dex.PressGenerate();
        WaitFor(() => !dex.Working, "the living Pokédex");
        Check(dex.CanPlace && dex.Ready != 0 && dex.RowCount >= dex.Ready, $"The living Pokédex reports what it made and skipped ({dex.StatusText})");
        Check(dex.RowCount == dex.Ready || dex.Cell(0, 2).StartsWith(T("Omitido: {0}", ""), StringComparison.Ordinal), "What was skipped comes first, and says why");
        Check(open.Pokemon.Count == before && !open.HasChanges, "Nothing is placed until asked");
        int ready = dex.Ready;
        dex.PressPlace();
        Check(open.Pokemon.Count == before + ready && open.BoxChangeCount == ready && open.HasChanges && !dex.CanPlace, "Placing them stages them all");
        Check(open.Pokemon.Where(p => p.IsNew).All(p => Catalog.CheckLegality(p).Valid), "Everything placed is legal");
        Check(open.BuildOutput().Length == open.OriginalBytes.Length, "The living Pokédex can be written");
        dex.Close();
        Tool(T("Deshacer el último cambio de cajas"));
        Check(!open.HasChanges && !save.Enabled && open.Pokemon.Count == before, "One undo takes the whole living Pokédex back");
        ShowBox(0); Notify("");
    }

    // Both tools full of data in every language, for CheckToolLayouts to measure.
    void InspectGenerators(Action<ToolWindow, string> Inspect)
    {
        var open = document!;
        var single = OpenFromMenu<ShowdownWindow>(T("Generar desde Showdown…"));
        single.PressGenerate("Pikachu\nLevel: 30");
        WaitFor(() => !single.Working, "the generated set");
        single.PressGenerate("Mewtwo\nLevel: 1");
        WaitFor(() => !single.Working, "the impossible set");
        Inspect(single, "showdown");
        var dex = OpenFromMenu<LivingDexWindow>(T("Pokédex viviente…"));
        Inspect(dex, "living-dex-start");
        dex.PressGenerate();
        WaitFor(() => !dex.Working, "the living Pokédex");
        Inspect(dex, "living-dex");
        dex.PressPlace();
        Inspect(dex, "living-dex-placed");
        dex.Close();
        if (open.CanUndoLayout) open.ResetLayout();
        ShowArranged(null); Notify("");
    }

    // Everything the boxes can do, through the same grids and menus a person uses. Leaves the document as it found it.
    void RunBoxChecks(Action<bool, string> Check, string exportFolder)
    {
        var open = document!;
        void Menu(SlotGrid grid, int index, string text)
        {
            BuildSlotMenu(grid, index);
            var item = slotMenu.Items.Cast<ToolStripItem>().FirstOrDefault(i => i.Text == text) ?? throw new Exception("No menu entry: " + text);
            item.PerformClick();
        }
        void Tool(string text) => AllTools().First(i => i.Text == text).PerformClick();
        search.Text = ""; tabs.SelectedIndex = 0; ShowBox(0);
        var boxed = open.Pokemon.First(p => !p.IsParty);
        var teamFirst = open.TeamSlots()[0]!; var teamSecond = open.TeamSlots()[1]!;
        int free = boxGrid.Slots.ToList().FindIndex(s => s == null);
        Check(free > boxed.Index, "The open box has a free place after its Pokémon");

        boxGrid.DragSlot(boxed.Index, boxGrid, free);
        Check(boxed.Index == free && boxGrid.Slots[free] == boxed && open.HasChanges && save.Enabled && current == boxed, "Dragging a Pokémon to an empty place stages the move and enables Save");
        Check(Text.Contains("1 ") && open.BoxChangeCount == 1, "The title counts the move as a pending change");
        BuildSlotMenu(boxGrid, free);
        Check(slotMenu.Items.Cast<ToolStripItem>().Any(i => i.Text == T("Deshacer el último cambio de cajas")), "The slot menu offers to undo");
        Tool(T("Deshacer el último cambio de cajas"));
        Check(boxed.Index == 0 && !open.HasChanges && !save.Enabled && boxGrid.Slots[0] == boxed, "Undo puts the Pokémon back and leaves nothing pending");

        teamGrid.DragSlot(0, teamGrid, 1);
        Check(open.TeamSlots()[0] == teamSecond && open.TeamSlots()[1] == teamFirst, "Dragging inside the team swaps two places");
        teamGrid.DragSlot(0, teamGrid, 1);
        Check(open.TeamSlots()[0] == teamFirst && !open.HasChanges, "Swapping them back leaves nothing pending");

        boxGrid.DragSlot(0, teamGrid, 0);
        Check(open.TeamSlots()[0] == boxed && teamFirst.Place == new SlotPlace(false, 0) && boxGrid.Slots[0] == teamFirst, "Dragging from the box onto the team swaps them");
        Check(open.DirtyCount == 0 && open.BuildOutput().Length == open.OriginalBytes.Length, "That swap can be written");
        Tool(T("Deshacer el último cambio de cajas"));

        Pick(boxed); ShowBox(1);
        Check(current == boxed && box == 1, "Turning the page keeps the selection");
        Menu(boxGrid, 3, T("Mover aquí a {0}", boxed.DisplayName));
        Check(boxed.Index == open.Save.BoxSlotCount + 3 && box == 1 && current == boxed, "The menu of an empty place in another box moves the selection there");
        Check((string)boxPicker.Items[0]! == open.BoxLabel(0) && (string)boxPicker.Items[1]! == open.BoxLabel(1) && open.BoxLabel(0).Contains("0 /"), "The box list counts the move");
        Tool(T("Deshacer el último cambio de cajas"));
        Check(boxed.Index == 0 && !open.HasChanges, "That move can be undone");
        ShowBox(0); Pick(teamSecond);
        Menu(boxGrid, 0, T("Cambiar de lugar con {0}", teamSecond.DisplayName));
        Check(teamSecond.Place == new SlotPlace(false, 0) && boxed.Place == new SlotPlace(true, 1), "The menu of a Pokémon swaps it with the selected one");
        Tool(T("Deshacer el último cambio de cajas"));
        Check(!open.HasChanges, "That swap can be undone");
        Pick(boxed); ShowBox(0);

        Menu(boxGrid, free, T("Clonar aquí a {0}", boxed.DisplayName));
        var clone = boxGrid.Slots[free];
        Check(clone != null && clone != boxed && clone.IsNew && current == clone && open.Pokemon.Count == 8 && count.Text.Contains("8"), "The menu of an empty place clones the selected Pokémon there");
        Check(open.BoxLabel(0).Contains("2 /") && (string)boxPicker.Items[0]! == open.BoxLabel(0), "The box list counts the clone");
        Menu(boxGrid, free, T("Borrar"));
        Check(open.Pokemon.Count == 7 && boxGrid.Slots[free] == null && !open.HasChanges && current != clone, "Deleting the clone leaves nothing pending and selects someone else");

        Menu(boxGrid, 0, T("Borrar"));
        Check(open.Pokemon.Count == 6 && boxGrid.Slots[0] == null && open.HasChanges && status.Text.Contains(boxed.DisplayName), "The menu of a Pokémon deletes it, pending until saved");
        Tool(T("Deshacer el último cambio de cajas"));
        Check(open.Pokemon.Count == 7 && boxGrid.Slots[0] == boxed && !open.HasChanges, "Undo brings the deleted Pokémon back");

        string file = Path.Combine(exportFolder, "editrainer-box-check." + boxed.Original.Extension);
        ExportTo(boxed, file);
        Check(File.Exists(file), "A Pokémon can be exported to a file");
        Arrive(() => open.Import(file, new SlotPlace(false, free)));
        Check(boxGrid.Slots[free] is { IsNew: true } imported && imported.Original.Species == boxed.Original.Species && current == imported, "That file comes back into an empty place");
        File.Delete(file);
        Tool(T("Deshacer todos los cambios de cajas"));
        Check(!open.HasChanges && open.Pokemon.Count == 7, "Undoing every box change leaves nothing pending");

        Arrive(() => open.Clone(boxed, new SlotPlace(false, 2)));
        Arrive(() => open.Clone(teamFirst, new SlotPlace(false, 1)));
        var before = open.BoxSlots(0).Take(3).ToList();
        Tool(T("Ordenar caja por nivel"));
        var sorted = open.BoxSlots(0).Take(3).Select(p => p!.Edit.Level).ToList();
        Check(sorted.SequenceEqual(sorted.OrderDescending()) && open.CanUndoLayout, "The Tools menu sorts the open box");
        Tool(T("Ordenar caja por Pokédex"));
        Check(open.BoxSlots(0).Take(3).Select(p => p!.Original.Species).SequenceEqual(before.Select(p => p!.Original.Species).Order()), "Sorting by Pokédex number puts the box in that order");
        Tool(T("Deshacer todos los cambios de cajas"));
        Check(!open.HasChanges, "Sorting can be undone");

        search.Text = boxed.SpeciesName;
        Check(!boxGrid.Movable && !teamGrid.Movable, "A search has no places to drag to");
        search.Text = "";
        Check(boxGrid.Movable && teamGrid.Movable, "Clearing the search brings dragging back");
        Pick(teamFirst);
        for (int i = open.TeamSlots().Count(p => p != null) - 1; i > 0; i--) Arrange(() => open.Delete(open.TeamSlots()[1]!));
        Check(open.TeamSlots().Count(p => p != null) == 1, "The team can shrink down to one Pokémon");
        Menu(teamGrid, 0, T("Borrar"));
        Check(open.TeamSlots()[0] != null && status.ForeColor == Theme.Danger && status.Text.Length != 0, "The last Pokémon of the team stays, and the app says why");
        Tool(T("Deshacer todos los cambios de cajas"));
        Check(!open.HasChanges && !save.Enabled && open.Pokemon.Count == 7, "Everything comes back");
        ShowBox(0); Notify("");
    }

    // Each sentence a box action can leave in the status line, at its longest, has to fit there.
    void CheckBoxMessages(Action<bool, string> Check)
    {
        const string name = "Wwwwwwwwwwww";
        string place = T("Caja {0} · {1}", 32, 30);
        string[] messages = [
            T("{0} clonado en {1} sin {2}, porque no hay otra unidad en la bolsa. Guarda para aplicarlo.", name, place, name + name),
            T("{0} borrado y {1} devuelto a la bolsa. Guarda para aplicarlo o deshazlo con clic derecho.", name, name + name),
            T("{0} convertido de {1} a {2} e importado en {3}.", name, "PK3", "PA9", place) + " " + T("Llega sin {0}: no hay en la bolsa. Guarda para aplicarlo.", name + name),
            T("{0} lleva {1}, que solo puede tener un Pokémon. Retíralo antes de clonarlo.", name, name + name),
            T("{0} está en un equipo de combate. Sácalo de ahí en el juego antes de cambiarlo de lugar.", name),
            T("{0} lleva una carta. Quítasela en el juego antes de cambiarlo de lugar.", name),
            T("Las partidas japonesas de Game Boy no se mezclan con las de otros idiomas."),
            T("{0} generado en {1}.", name, place) + " " + T("Llega sin {0}: no hay en la bolsa. Guarda para aplicarlo.", name + name),
            T("Pokémon colocados: {0}. Guarda para aplicarlos, o quítalos todos con «Deshacer el último cambio de cajas».", 960),
        ];
        foreach (var message in messages)
        {
            status.Text = message;
            var size = TextRenderer.MeasureText(status.Text, status.Font, new Size(status.ClientSize.Width, int.MaxValue), TextFormatFlags.WordBreak);
            Check(size.Height <= status.ClientSize.Height + 1, $"The status line holds: {message} ({size.Height} of {status.ClientSize.Height})");
        }
        status.Text = "";
    }
}
