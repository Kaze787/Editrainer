using PKHeX.Core;
using System.Buffers.Binary;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>A place in the save: one of the team's six, or one of the box places, counted across every box.</summary>
public readonly record struct SlotPlace(bool IsParty, int Index);

public enum BoxOrder { Pokedex, Level, Name }

/// <summary>Someone new in the save, and what to tell about it.</summary>
public sealed record Arrival(PokemonEntry Entry, string Message);

/// <summary>Moving, cloning, deleting, importing and sorting. All of it stays a draft until the save is written.</summary>
public sealed partial class SaveDocument
{
    const int ListPointers = 7;   // Let's Go's six team places, then its starter
    const StorageSlotSource TeamFlags = StorageSlotSource.Party1 | StorageSlotSource.Party2 | StorageSlotSource.Party3
        | StorageSlotSource.Party4 | StorageSlotSource.Party5 | StorageSlotSource.Party6;

    List<PokemonEntry> loaded = [];
    HashSet<SlotPlace> unreadable = [];
    readonly Stack<List<(PokemonEntry Entry, SlotPlace Place)>> layoutHistory = new();

    // Game Boy boxes and Let's Go's whole storage are lists, which the game can't read with a gap in them.
    bool IsListStorage => Rules.IsGameBoy || Save is SAV7b;
    IEnumerable<PokemonEntry> Deleted => loaded.Except(Pokemon);
    IEnumerable<SlotPlace> Vacated => loaded.Select(e => e.Origin!.Value).Except(Pokemon.Select(p => p.Place));
    IEnumerable<SlotPlace> ChangedPlaces => Pokemon.Where(p => p.Dirty || p.Moved).Select(p => p.Place).Concat(Vacated);

    public bool LayoutChanged => loaded.Count != Pokemon.Count || Pokemon.Any(p => p.Moved);
    public bool CanUndoLayout => layoutHistory.Count != 0;
    public int BoxChangeCount => Pokemon.Count(p => p.Moved && !p.Dirty) + Deleted.Count();

    // Colosseum, XD and the side games tie a Pokémon to more than its place, and none of them can be tested here.
    public bool CanArrange => Save is SAV1 or SAV2 or SAV3 or SAV4 or SAV5 or SAV6 or SAV7 or SAV7b or SAV8SWSH or SAV8BS or SAV8LA or SAV9SV or SAV9ZA
        && Save.HasBox && !(IsListStorage && unreadable.Count != 0);

    // A box the game hasn't opened yet can't be reached from it, so nobody is sent there.
    int OpenBoxes
    {
        get
        {
            try { return Save.BoxesUnlocked is > 0 and var open ? Math.Min(open, Save.BoxCount) : Save.BoxCount; }
            catch (Exception e) when (e is ArgumentException or InvalidCastException) { return Save.BoxCount; }
        }
    }

    public PokemonEntry? At(SlotPlace place) => Pokemon.FirstOrDefault(p => p.Place == place);

    HashSet<int> TakenPlaces() => Pokemon.Where(p => !p.IsParty).Select(p => p.Index).Concat(unreadable.Where(p => !p.IsParty).Select(p => p.Index)).ToHashSet();

    /// <summary>The first free place from this box on, going round to the first box after the last.</summary>
    public SlotPlace? FreePlace(int box)
    {
        var used = TakenPlaces();
        int count = OpenBoxes * Save.BoxSlotCount, start = box < OpenBoxes ? box * Save.BoxSlotCount : 0;
        for (int i = 0; i < count; i++)
            if (!used.Contains((start + i) % count)) return new(false, (start + i) % count);
        return null;
    }

    void RequireArrange()
    {
        if (!CanArrange) throw new InvalidOperationException(T("Esta partida no permite reorganizar sus Pokémon."));
    }

    void Require(SlotPlace place)
    {
        RequireArrange();
        int limit = place.IsParty ? TeamSize : Save.BoxCount * Save.BoxSlotCount;
        if (place.Index < 0 || place.Index >= limit || place.IsParty && !Save.HasParty || unreadable.Contains(place))
            throw new InvalidOperationException(T("Ese lugar no se puede usar."));
        if (!place.IsParty && place.Index / Save.BoxSlotCount >= OpenBoxes)
            throw new InvalidOperationException(T("El juego aún no ha abierto esa caja."));
    }

    void RequireFree(SlotPlace place)
    {
        Require(place);
        if (At(place) != null) throw new InvalidOperationException(T("Ese lugar ya está ocupado."));
    }

    StorageSlotSource Flags(PokemonEntry e) => e.Origin is { IsParty: false } place ? Save.GetBoxSlotFlags(place.Index) : StorageSlotSource.None;

    // What keeps a Pokémon where it is: something else in the save points at that place, or at that letter.
    string? WhyFixed(PokemonEntry e)
    {
        if (Rules.IsMail(e.Edit.HeldItem)) return T("{0} lleva una carta. Quítasela en el juego antes de cambiarlo de lugar.", e.DisplayName);
        if (e.Origin is not { IsParty: false } place || !Save.IsBoxSlotOverwriteProtected(place.Index)) return null;
        return Flags(e).HasFlag(StorageSlotSource.Starter) ? T("{0} es tu compañero inicial y debe quedarse donde está.", e.DisplayName)
            : T("{0} está en un equipo de combate. Sácalo de ahí en el juego antes de cambiarlo de lugar.", e.DisplayName);
    }

    // Each change happens on the real layout, and comes back if it leaves something the game could not read.
    void Change(Action change)
    {
        var before = Layout();
        string? supply = ValidateSupply();
        try
        {
            change();
            Compact();
            if (LayoutError() is { } layout) throw new InvalidOperationException(layout);
            if (ValidateSupply() is { } error && error != supply) throw new InvalidOperationException(error);
        }
        catch (InvalidOperationException) { Restore(before); throw; }
        if (!Layout().SequenceEqual(before)) layoutHistory.Push(before);
    }

    List<(PokemonEntry Entry, SlotPlace Place)> Layout() => Pokemon.Select(p => (p, p.Place)).ToList();

    void Restore(List<(PokemonEntry Entry, SlotPlace Place)> layout)
    {
        foreach (var (entry, place) in layout) { entry.IsParty = place.IsParty; entry.Index = place.Index; }
        Pokemon = layout.Select(x => x.Entry).ToList();
    }

    void Compact()
    {
        if (!unreadable.Any(p => p.IsParty)) Renumber(Pokemon.Where(p => p.IsParty), 0);
        if (Save is SAV7b) Renumber(Pokemon.Where(p => !p.IsParty), 0);
        else if (Rules.IsGameBoy)
            foreach (var group in Pokemon.Where(p => !p.IsParty).GroupBy(BoxOf).ToList()) Renumber(group, group.Key * Save.BoxSlotCount);
    }

    static void Renumber(IEnumerable<PokemonEntry> group, int start)
    {
        foreach (var e in group.OrderBy(p => p.Index).ToList()) e.Index = start++;
    }

    string? LayoutError()
    {
        if (!LayoutChanged) return null;
        var team = Pokemon.Where(p => p.IsParty).ToList();
        if (team.Count > TeamSize) return T("El equipo ya tiene seis Pokémon.");
        if (Save.HasParty && Save.PartyCount != 0 && team.All(p => p.Original.IsEgg)) return T("El equipo debe conservar al menos un Pokémon que no sea un huevo.");
        if (unreadable.Any(p => p.IsParty) && Vacated.Concat(Pokemon.Where(p => p.Moved).Select(p => p.Place)).Any(p => p.IsParty))
            return T("El equipo tiene datos dañados y no se puede reorganizar.");
        var places = Pokemon.Select(p => p.Place).ToList();
        return places.Distinct().Count() != places.Count || places.Any(unreadable.Contains) ? T("Ese lugar ya está ocupado.") : null;
    }

    /// <summary>Takes a Pokémon to a place, swapping with whoever is there. A list leaves no gap: it closes up behind it.</summary>
    public string Move(PokemonEntry entry, SlotPlace target)
    {
        Require(target);
        if (entry.Place == target) return "";
        var other = At(target);
        if (other != null) Require(entry.Place);
        if (WhyFixed(entry) is { } held) throw new InvalidOperationException(held);
        if (other != null && WhyFixed(other) is { } occupied) throw new InvalidOperationException(occupied);
        Change(() => {
            if (other != null) { other.IsParty = entry.IsParty; other.Index = entry.Index; }
            entry.IsParty = target.IsParty; entry.Index = target.Index;
        });
        return other == null ? T("{0} va a {1}. Guarda para aplicarlo.", entry.DisplayName, entry.Location)
            : T("{0} y {1} cambian de lugar. Guarda para aplicarlo.", entry.DisplayName, other.DisplayName);
    }

    /// <summary>Takes a Pokémon out of the save. Its item goes back to the bag, as when it is unequipped.</summary>
    public string Delete(PokemonEntry entry)
    {
        RequireArrange();
        if (WhyFixed(entry) is { } held) throw new InvalidOperationException(held);
        if ((Flags(entry) & TeamFlags) != 0) throw new InvalidOperationException(T("{0} está en tu equipo. Cámbialo en el juego antes de borrarlo.", entry.DisplayName));
        int item = entry.Edit.HeldItem;
        Change(() => Pokemon.Remove(entry));
        return item != 0 && Items.Any(i => i.Id == item) ? T("{0} borrado y {1} devuelto a la bolsa. Guarda para aplicarlo o deshazlo con clic derecho.", entry.DisplayName, Rules.ItemName(item))
            : T("{0} borrado. Guarda para aplicarlo o deshazlo con clic derecho.", entry.DisplayName);
    }

    /// <summary>Copies a Pokémon into an empty place. An item needs its own unit: the bag gives it, or the clone goes without.</summary>
    public Arrival Clone(PokemonEntry source, SlotPlace target)
    {
        RequireFree(target);
        if (source.Dirty && Catalog.Validate(source) is { } invalid) throw new InvalidOperationException(invalid);
        int item = source.Edit.HeldItem;
        if (Rules.IsMail(item)) throw new InvalidOperationException(T("{0} lleva una carta. Quítasela en el juego antes de cambiarlo de lugar.", source.DisplayName));
        if (item != 0 && IsUniqueHeldItem((ushort)item))
            throw new InvalidOperationException(T("{0} lleva {1}, que solo puede tener un Pokémon. Retíralo antes de clonarlo.", source.DisplayName, Rules.ItemName(item)));
        var (entry, missing) = Arrive(source.Preview(), target);
        return new(entry, missing == null ? T("{0} clonado en {1}. Guarda para aplicarlo.", source.DisplayName, entry.Location)
            : T("{0} clonado en {1} sin {2}, porque no hay otra unidad en la bolsa. Guarda para aplicarlo.", source.DisplayName, entry.Location, missing));
    }

    /// <summary>Brings a Pokémon from a file into an empty place, converted to this game's format where PKHeX knows the way.</summary>
    public Arrival Import(string path, SlotPlace target)
    {
        RequireFree(target);
        var file = ReadFile(path);
        var (entry, missing) = Arrive(ForThisGame(file, target), target);
        string from = file.GetType().Name, to = Save.PKMType.Name;
        string where = from == to ? T("{0} importado en {1}.", entry.DisplayName, entry.Location) : T("{0} convertido de {1} a {2} e importado en {3}.", entry.DisplayName, from, to, entry.Location);
        return new(entry, where + " " + (missing == null ? T("Guarda para aplicarlo.") : T("Llega sin {0}: no hay en la bolsa. Guarda para aplicarlo.", missing)));
    }

    (PokemonEntry Entry, string? Missing) Arrive(PKM pokemon, SlotPlace target)
    {
        int item = Rules.HasHeldItems && !pokemon.IsEgg ? pokemon.HeldItem : 0;
        if (Rules.HasHeldItems && pokemon.HeldItem != 0) { pokemon.HeldItem = 0; pokemon.RefreshChecksum(); }
        var entry = new PokemonEntry(Rules, target.IsParty, target.Index, pokemon, isNew: true);
        Change(() => Pokemon.Add(entry));
        if (item == 0) return (entry, null);
        try { EquipItem(entry, item); return (entry, null); }
        catch (InvalidOperationException) { return (entry, Rules.ItemName(item) is { Length: > 0 } name ? name : item.ToString()); }
    }

    PKM ReadFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && EntityDetection.IsSizePlausible(info.Length)
                && FileUtil.TryGetPKM(File.ReadAllBytes(path), out var pokemon, Path.GetExtension(path), Save) && pokemon.Species != 0 && pokemon.ChecksumValid) return pokemon;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException(T("No se pudo leer ese archivo."));
        }
        throw new InvalidOperationException(T("Ese archivo no contiene un Pokémon válido."));
    }

    PKM ForThisGame(PKM file, SlotPlace target)
    {
        string name = file.Species < Catalog.Names.specieslist.Length ? Catalog.Names.specieslist[file.Species] : file.Species.ToString();
        // This asks first whether a way exists: converting straight away never comes back from some of the ways that don't.
        if (!EntityConverter.TryMakePKMCompatible(file, Save.BlankPKM, out var result, out var converted))
            throw new InvalidOperationException(result switch {
                EntityConverterResult.IncompatibleSpecies => T("{0} no existe en {1}.", name, Rules.FullName),
                EntityConverterResult.IncompatibleForm => T("Esa forma de {0} no se puede llevar a {1}.", name, Rules.FullName),
                EntityConverterResult.IncompatibleLanguageGB => T("Las partidas japonesas de Game Boy no se mezclan con las de otros idiomas."),
                _ => T("Un Pokémon de {0} no se puede llevar a {1}.", file.GetType().Name, Rules.FullName),
            });
        var pokemon = converted.Clone();
        if (pokemon.Species > Save.MaxSpeciesID || !Rules.Personal.IsPresentInGame(pokemon.Species, Rules.HasStoredForm ? pokemon.Form : (byte)0))
            throw new InvalidOperationException(T("{0} no existe en {1}.", name, Rules.FullName));
        if (!Rules.CanShow(pokemon.Species)) throw new InvalidOperationException(Rules.HiddenReason(name));
        if (pokemon.Moves.Any(m => m > Save.MaxMoveID) || Rules.HasAbility && pokemon.Ability > Save.MaxAbilityID)
            throw new InvalidOperationException(T("Ese Pokémon tiene datos que {0} no admite.", Rules.FullName));
        Save.AdaptToSaveFile(pokemon, target.IsParty, EntityImportOption.Enable);
        pokemon.ForcePartyData();   // Z-A and Let's Go keep these numbers in the boxes too
        pokemon.RefreshChecksum();
        return pokemon;
    }

    /// <summary>A Pokémon as a file, the way it would be saved.</summary>
    public static byte[] PokemonFile(PokemonEntry entry)
    {
        if (entry.Dirty && Catalog.Validate(entry) is { } invalid) throw new InvalidOperationException(invalid);
        var pokemon = entry.Preview();
        pokemon.ForcePartyData();
        var data = new byte[pokemon.SIZE_PARTY];
        pokemon.WriteDecryptedDataParty(data);
        return data;
    }

    public static string PokemonFileName(PokemonEntry entry) => PathUtil.CleanFileName(entry.Preview().FileName);

    /// <summary>Puts a box in order. Whoever has to stay stays, and the rest fill the places around them.</summary>
    public string SortBox(int box, BoxOrder order)
    {
        RequireArrange();
        int first = box * Save.BoxSlotCount;
        var boxed = Pokemon.Where(p => !p.IsParty && BoxOf(p) == box).ToList();
        var staying = boxed.Where(p => WhyFixed(p) != null).ToList();
        var taken = staying.Select(p => p.Index).Concat(unreadable.Where(p => !p.IsParty).Select(p => p.Index)).ToHashSet();
        var places = Enumerable.Range(first, Save.BoxSlotCount).Where(i => !taken.Contains(i)).ToList();
        var sorted = Sorted(boxed.Except(staying), order).ToList();
        Change(() => { for (int i = 0; i < sorted.Count; i++) sorted[i].Index = places[i]; });
        return T("{0} ordenada. Guarda para aplicarlo.", BoxName(box));
    }

    static IEnumerable<PokemonEntry> Sorted(IEnumerable<PokemonEntry> group, BoxOrder order) => order switch {
        BoxOrder.Level => group.OrderByDescending(p => p.Edit.Level).ThenBy(p => p.Original.Species).ThenBy(p => p.Edit.Form),
        BoxOrder.Name => group.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Original.Species),
        _ => group.OrderBy(p => p.Original.Species).ThenBy(p => p.Edit.Form).ThenByDescending(p => p.Edit.Level),
    };

    public void UndoLayout() => RestoreLayout(layoutHistory.Peek(), () => layoutHistory.Pop());

    public void ResetLayout() => RestoreLayout(layoutHistory.Last(), layoutHistory.Clear);

    // An item can't come back with its Pokémon if it has gone somewhere else meanwhile.
    void RestoreLayout(List<(PokemonEntry Entry, SlotPlace Place)> layout, Action done)
    {
        var current = Layout();
        Restore(layout);
        if (ValidateSupply() is { } error) { Restore(current); throw new InvalidOperationException(error); }
        done();
    }

    // An item the catalog doesn't know leaves with its Pokémon instead of entering the bag.
    IEnumerable<ushort> ItemsOfDeleted() => Deleted.SelectMany(p => new[] { p.Edit.HeldItem, p.Baseline.HeldItem })
        .Where(id => id != 0 && Items.Any(i => i.Id == id)).Select(id => (ushort)id);

    // What a place has to hold once it is written.
    PKM Expected(PokemonEntry e)
    {
        var pokemon = e.Dirty ? Catalog.Apply(e) : e.Original.Clone();
        if (e.IsParty) pokemon.ForcePartyData();
        return pokemon;
    }

    void WriteStorage(SaveFile output)
    {
        foreach (var e in Pokemon.Where(x => x.Dirty || x.Moved).OrderBy(x => x.Index))
        {
            if (e.IsParty) output.SetPartySlotAtIndex(Expected(e), e.Index, EntityImportSettings.None);
            else output.SetBoxSlotAtIndex(Expected(e), e.Index, EntityImportSettings.None);
        }
        // Z-A keeps a template in its empty places, not zeros, so a freed place takes that same one.
        int template = output is SAV9ZA ? Enumerable.Range(0, Save.SlotCount).FirstOrDefault(i => Save.GetBoxSlotAtIndex(i).Species == 0, -1) : -1;
        foreach (var place in Vacated.OrderBy(p => p.Index)) Clear(output, place, template);
        if (output is SAV7b letsGo && LayoutChanged) WriteListHeader(letsGo);
    }

    void Clear(SaveFile output, SlotPlace place, int template)
    {
        if (place.IsParty) output.SetPartySlotAtIndex(output.BlankPKM, place.Index, EntityImportSettings.None);
        else output.SetBoxSlotAtIndex(output.BlankPKM, place.Index, EntityImportSettings.None);
        if (output is not SAV9ZA za) return;
        if (place.IsParty) za.PartyInfo.WriteEmpty(place.Index);
        else if (template >= 0) ZaSave!.BoxInfo.Data.Slice(Save.GetBoxSlotOffset(template), Save.SIZE_BOXSLOT).CopyTo(za.BoxInfo.Data[Save.GetBoxSlotOffset(place.Index)..]);
    }

    // Let's Go's team and starter are pointers into its list, so they follow their Pokémon along it.
    void WriteListHeader(SAV7b output)
    {
        var header = output.Storage.Data;
        for (int i = 0; i < ListPointers; i++)
        {
            var pointer = header[(i * sizeof(ushort))..];
            var place = new SlotPlace(false, BinaryPrimitives.ReadUInt16LittleEndian(pointer));
            if (Pokemon.FirstOrDefault(p => p.Origin == place) is { } entry) BinaryPrimitives.WriteUInt16LittleEndian(pointer, (ushort)entry.Index);
        }
        output.Storage.Count = Pokemon.Count;
    }

    /// <summary>Proves that every place of a written save holds what it should, and that no other place changed.</summary>
    public void VerifyStorage(SaveFile written)
    {
        if (written.PartyCount != (Save.HasParty ? Pokemon.Count(p => p.IsParty) + unreadable.Count(p => p.IsParty) : Save.PartyCount))
            throw new InvalidOperationException(T("Cambió la estructura de la partida."));
        foreach (var e in Pokemon)
        {
            var actual = e.IsParty ? written.GetPartySlotAtIndex(e.Index) : written.GetBoxSlotAtIndex(e.Index);
            var expected = Expected(e);
            if (Rules.IsZA ? !actual.ChecksumValid || !actual.Data.SequenceEqual(expected.Data) : !SamePokemon(actual, expected))
                throw new InvalidOperationException(T("No se pudo comprobar {0} después de guardar.", e.DisplayName));
        }
        var held = Pokemon.Select(p => p.Place).ToHashSet();
        var freed = Vacated.ToHashSet();
        void CheckFree(SlotPlace place, PKM before, PKM after)
        {
            if (held.Contains(place)) return;
            // A Game Boy list is rebuilt on every write, so only what its places hold can be compared.
            bool same = freed.Contains(place) || Rules.IsGameBoy ? after.Species == 0 : after.Data.SequenceEqual(before.Data);
            if (!same) throw new InvalidOperationException(T("Se modificó un espacio vacío de las cajas."));
        }
        if (Save.HasParty)
            for (int i = 0; i < TeamSize; i++) CheckFree(new(true, i), Save.GetPartySlotAtIndex(i), written.GetPartySlotAtIndex(i));
        var boxedBefore = Save.BoxData; var boxedAfter = written.BoxData;
        for (int i = 0; i < boxedBefore.Count; i++) CheckFree(new(false, i), boxedBefore[i], boxedAfter[i]);
        if (written is SAV9ZA za) VerifySlotFooters(za);
        if (written is SAV7b letsGo && LayoutChanged && (letsGo.Storage.Count != Pokemon.Count || Pokemon.Any(p => letsGo.GetBoxSlotFlags(p.Index) != Flags(p))))
            throw new InvalidOperationException(T("Cambió la estructura de la partida."));
    }

    // Each Z-A place also has bytes around its Pokémon that reading it back doesn't show.
    void VerifySlotFooters(SAV9ZA written)
    {
        var changed = ChangedPlaces.ToHashSet();
        var original = ZaSave!;
        int size = written.SIZE_BOXSLOT;
        for (int i = 0; i < written.SlotCount; i++)
            if (!changed.Contains(new(false, i)) && !written.BoxInfo.Data.Slice(i * size, size).SequenceEqual(original.BoxInfo.Data.Slice(i * size, size)))
                throw new InvalidOperationException(T("Se detectó un cambio fuera de los datos que editaste."));
        for (int i = 0; i < TeamSize; i++)
            if (!changed.Contains(new(true, i)) && !written.PartyInfo.GetSlot(i).Span.SequenceEqual(original.PartyInfo.GetSlot(i).Span))
                throw new InvalidOperationException(T("Se detectó un cambio fuera de los datos que editaste."));
    }
}
