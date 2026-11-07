using PKHeX.Core;
using static Editrainer.Strings;

namespace Editrainer;

sealed class EquipmentPanel : UserControl
{
    SaveDocument? document;
    PokemonEntry? pokemon;
    bool binding;
    readonly MovePicker picker = new();
    readonly CheckBox megasOnly = new() { Text = T("Solo megapiedras"), AutoSize = true, Anchor = AnchorStyles.Left,
        Font = Theme.Font(9), Margin = new Padding(12, 8, 3, 8) };
    readonly Label currentItem = Theme.Label(T("Objeto actual: {0}", T("ninguno")), 9, true);
    readonly Label availability = Theme.Label("", 9, color: Theme.Muted);
    readonly NumericUpDown friendship = new() { Minimum = 0, Maximum = 255, Font = Theme.Font(12), Width = 120,
        TextAlign = HorizontalAlignment.Center, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(4, 7, 14, 7) };
    readonly Button equip = Theme.Button(T("Equipar"), true), remove = Theme.Button(T("Quitar")), openBag = Theme.Button(T("Bolsa")), maxFriendship = Theme.Button(T("Máximo (255)"), true);
    public event EventHandler? DraftChanged;
    public event EventHandler? BagChanged;
    public event Action<ushort>? BagRequested;
    public event Action<string>? StatusMessage;

    public EquipmentPanel()
    {
        Dock = DockStyle.Top; BackColor = Theme.Surface; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Margin = Padding.Empty;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        for (int i = 0; i < 3; i++) layout.RowStyles.Add(new(SizeType.AutoSize)); Controls.Add(layout);
        var selection = Row(5); selection.ColumnStyles.Add(new(SizeType.Absolute, 107)); selection.ColumnStyles.Add(new(SizeType.Percent, 100));
        selection.ColumnStyles.Add(new(SizeType.AutoSize)); selection.ColumnStyles.Add(new(SizeType.AutoSize)); selection.ColumnStyles.Add(new(SizeType.AutoSize));
        selection.Controls.Add(Theme.Label(T("Objeto"), 10, true), 0, 0); selection.Controls.Add(picker, 1, 0);
        selection.Controls.Add(equip, 2, 0); selection.Controls.Add(remove, 3, 0); selection.Controls.Add(openBag, 4, 0);
        layout.Controls.Add(selection, 0, 0);
        var friendshipRow = Row(4); friendshipRow.ColumnStyles.Add(new(SizeType.Absolute, 107)); friendshipRow.ColumnStyles.Add(new(SizeType.Absolute, 105));
        friendshipRow.ColumnStyles.Add(new(SizeType.AutoSize)); friendshipRow.ColumnStyles.Add(new(SizeType.Percent, 100));
        var friendLabel = Theme.Label(T("Amistad"), 10, true);
        friendshipRow.Controls.Add(friendLabel, 0, 0); friendshipRow.Controls.Add(friendship, 1, 0); friendshipRow.Controls.Add(maxFriendship, 2, 0);
        layout.Controls.Add(friendshipRow, 0, 1);
        var details = Row(2); details.ColumnStyles.Add(new(SizeType.Percent, 100)); details.ColumnStyles.Add(new(SizeType.AutoSize));
        var text = Row(1); text.RowCount = 2; text.RowStyles.Add(new(SizeType.AutoSize)); text.RowStyles.Add(new(SizeType.AutoSize));
        currentItem.MinimumSize = new Size(0, 22); availability.MinimumSize = new Size(0, 42);
        text.Controls.Add(currentItem, 0, 0); text.Controls.Add(availability, 0, 1);
        details.Controls.Add(text, 0, 0); details.Controls.Add(megasOnly, 1, 0); layout.Controls.Add(details, 0, 2);
        picker.MoveCommitted += (_, _) => { if (!binding) UpdateDetails(); };
        megasOnly.CheckedChanged += (_, _) => { if (!binding && document != null && pokemon != null) Bind(document, pokemon, true); };
        equip.Click += (_, _) => ApplyEquipment(picker.SelectedChoice?.Id ?? 0);
        remove.Click += (_, _) => ApplyEquipment(0);
        openBag.Click += (_, _) => BagRequested?.Invoke((ushort)(picker.SelectedChoice?.Id ?? 0));
        friendship.ValueChanged += (_, _) => {
            if (binding || pokemon == null) return;
            pokemon.Edit.Friendship = (int)friendship.Value; maxFriendship.Enabled = friendship.Value != 255;
            DraftChanged?.Invoke(this, EventArgs.Empty);
        };
        maxFriendship.Click += (_, _) => friendship.Value = 255;
        equip.EnabledChanged += (_, _) => equip.BackColor = equip.Enabled ? Theme.Accent : Theme.Line;
        maxFriendship.EnabledChanged += (_, _) => maxFriendship.BackColor = maxFriendship.Enabled ? Theme.Accent : Theme.Line;
        Enabled = false;
    }

    static TableLayoutPanel Row(int columns) => new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = columns, RowCount = 1, Margin = Padding.Empty };

    public void Bind(SaveDocument doc, PokemonEntry entry, bool keepSelection = false)
    {
        int selected = keepSelection && pokemon == entry ? picker.SelectedChoice?.Id ?? entry.Edit.HeldItem : entry.Edit.HeldItem;
        document = doc; pokemon = entry; binding = true; Enabled = true; megasOnly.Visible = doc.Rules.IsZA;
        var choices = doc.GetHeldChoices().Where(c => !megasOnly.Checked || !doc.Rules.IsZA || c.Id == 0 || doc.IsMegaStoneItem((ushort)c.Id)).ToList();
        if (entry.Edit.HeldItem > 0 && !choices.Any(c => c.Id == entry.Edit.HeldItem))
            choices.Add(new(entry.Edit.HeldItem, doc.Rules.ItemName(entry.Edit.HeldItem)));
        if (!choices.Any(c => c.Id == selected)) selected = entry.Edit.HeldItem;
        picker.SetChoices(choices, selected); friendship.Value = entry.Edit.Friendship; binding = false; UpdateDetails();
    }

    void UpdateDetails()
    {
        if (document == null || pokemon == null) return;
        int id = picker.SelectedChoice?.Id ?? 0;
        currentItem.Text = T("Objeto actual: {0}", pokemon.Edit.HeldItem == 0 ? T("ninguno") : document.Rules.ItemName(pokemon.Edit.HeldItem));
        remove.Enabled = pokemon.Edit.HeldItem != 0; maxFriendship.Enabled = friendship.Value != 255;
        openBag.Enabled = id != 0; equip.Enabled = id != pokemon.Edit.HeldItem;
        if (id == 0) { availability.Text = T("Escribe el nombre de un objeto para buscarlo y equiparlo."); return; }
        if (id == pokemon.Edit.HeldItem) { availability.Text = T("Ya está equipado. «Quitar» lo devuelve a la bolsa."); return; }
        int count = document.GetItemCount((ushort)id);
        var holder = document.ItemHolder((ushort)id);
        if (document.IsUniqueHeldItem((ushort)id) && holder != null && holder != pokemon)
        {
            availability.Text = T("Lo lleva {0}. Retíralo de ese Pokémon para usarlo aquí.", holder.DisplayName); equip.Enabled = false;
        }
        else if (count < 1)
        {
            availability.Text = T("Sin unidades. Usa «Bolsa» para agregar este objeto."); equip.Enabled = false;
        }
        else availability.Text = T("En la bolsa: {0}. {1}", count, document.IsMegaStoneItem((ushort)id)
            ? T("Se asigna a un solo Pokémon.") : T("Equipar toma una unidad; quitar la devuelve."));
    }

    void ApplyEquipment(int id)
    {
        if (document == null || pokemon == null) return;
        try {
            document.EquipItem(pokemon, id); Bind(document, pokemon);
            BagChanged?.Invoke(this, EventArgs.Empty); DraftChanged?.Invoke(this, EventArgs.Empty);
            StatusMessage?.Invoke(id == 0 ? T("Objeto retirado y devuelto a la bolsa.") : T("{0} equipado en {1}.", document.Rules.ItemName(id), pokemon.DisplayName));
        }
        catch (InvalidOperationException ex) { StatusMessage?.Invoke(ex.Message); UpdateDetails(); }
    }

    internal void RunInteractionChecks(Action<bool, string> check)
    {
        if (document == null || pokemon == null) throw new InvalidOperationException("No hay Pokémon seleccionado.");
        var initial = pokemon.Edit.Friendship; maxFriendship.PerformClick();
        check(friendship.Value == 255 && pokemon.Edit.Friendship == 255 && pokemon.Preview().CurrentFriendship == 255,
            "Friendship maximum button updates the draft and Pokémon preview");
        friendship.Value = initial;
        document.SetItemCount(658, 1); picker.SelectMove(658); UpdateDetails();
        check(equip.Enabled, "Owned Mega Stone can be selected for equipping");
        equip.PerformClick();
        check(pokemon.Edit.HeldItem == 658 && document.GetItemCount(658) == 1 && document.IsItemHeld(658),
            "Equip button assigns a Mega Stone and keeps its one-unit ownership");
        remove.PerformClick(); check(pokemon.Edit.HeldItem == 0 && !document.IsItemHeld(658), "Remove button unassigns the Mega Stone");
        document.SetItemCount(658, 0); picker.SelectMove(658); UpdateDetails();
        check(!equip.Enabled && openBag.Enabled, "Missing held items can be opened in the bag and cannot be equipped");
        picker.Search("ampharos"); check(picker.Items.Count == 1 && picker.Items[0] is Choice c && c.Id == 658, "Held item selector searches names"); picker.CancelSearch();
        document.ResetPokemon(pokemon); document.ResetResources(); Bind(document, pokemon);
    }
}
