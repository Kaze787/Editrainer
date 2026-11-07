using PKHeX.Core;
using static Editrainer.Strings;

namespace Editrainer;

public enum BatchScope { Team, OpenBox, AllBoxes, Everyone }

/// <summary>One change for many Pokémon. What is left null or false stays as it is.</summary>
public sealed record BatchPlan
{
    public const int MaxFriendshipValue = 255;

    public int? IVs { get; init; }
    public int[]? EVs { get; init; }
    public int? Level { get; init; }
    public Nature? Nature { get; init; }
    public bool? Shiny { get; init; }
    public bool MaxFriendship { get; init; }
    public int? Ball { get; init; }
    public bool RemoveItem { get; init; }
    public bool ClearNickname { get; init; }
    public string? Trainer { get; init; }

    public bool IsEmpty => this == new BatchPlan();

    /// <summary>What no Pokémon of this game could take, whoever it is.</summary>
    public string? Problem(GameRules rules)
    {
        if (IVs is { } iv && (iv < 0 || iv > rules.MaxIV)) return T("Cada {0} debe estar entre 0 y {1}.", rules.IVName, rules.MaxIV);
        if (EVs is { } evs)
        {
            if (evs.Any(v => v < 0 || v > rules.MaxEV)) return T("Cada {0} debe estar entre 0 y {1}.", rules.EVName, rules.MaxEV);
            if (rules.MaxEVTotal != 0 && evs.Sum() > rules.MaxEVTotal) return T("Ese reparto suma {0}. El máximo es {1}.", evs.Sum(), rules.MaxEVTotal);
        }
        if (Level is < 1 or > Experience.MaxLevel) return T("El nivel debe estar entre 1 y {0}.", Experience.MaxLevel);
        return Trainer?.Trim().Length == 0 ? T("El nombre del entrenador no puede quedar vacío.") : null;
    }
}

/// <summary>A change one Pokémon didn't take, and why.</summary>
public sealed record BatchSkip(PokemonEntry Entry, string Change, string Reason);

public sealed record BatchResult(int Reached, int Changed, IReadOnlyList<BatchSkip> Skipped)
{
    public int SkippedPokemon => Skipped.Select(s => s.Entry).Distinct().Count();

    public string Summary
    {
        get
        {
            var parts = new List<string> { T("Pokémon cambiados: {0} de {1}.", Changed, Reached) };
            if (Skipped.Count != 0) parts.Add(T("Con omisiones: {0}.", SkippedPokemon));
            if (Changed != 0) parts.Add(T("Guarda para aplicarlo."));
            return string.Join(" ", parts);
        }
    }
}

// Either does what it says or leaves the draft alone and returns why not.
sealed record BatchStep(string Change, Func<PokemonEntry, string?> Run);

sealed record BatchChange(PokemonEntry Entry, Draft Before, Draft After)
{
    /// <summary>Back to the draft from before the batch. Whatever was edited by hand since then stays as it was left.</summary>
    public void Undo()
    {
        if (Entry.Edit.SameAs(After)) { Entry.Edit = Before.Copy(); return; }
        var d = Entry.Edit = Entry.Edit.Copy();
        d.Level = Back(d.Level, Before.Level, After.Level);
        d.Nature = Back(d.Nature, Before.Nature, After.Nature);
        d.IsShiny = Back(d.IsShiny, Before.IsShiny, After.IsShiny);
        d.Friendship = Back(d.Friendship, Before.Friendship, After.Friendship);
        d.Ball = Back(d.Ball, Before.Ball, After.Ball);
        d.HeldItem = Back(d.HeldItem, Before.HeldItem, After.HeldItem);
        d.Nickname = Back(d.Nickname, Before.Nickname, After.Nickname);
        d.Trainer = Back(d.Trainer, Before.Trainer, After.Trainer);
        for (int stat = 0; stat < d.IVs.Length; stat++)
        {
            d.IVs[stat] = Back(d.IVs[stat], Before.IVs[stat], After.IVs[stat]);
            d.EVs[stat] = Back(d.EVs[stat], Before.EVs[stat], After.EVs[stat]);
        }
        if (Entry.Rules.IsGameBoy) d.IsShiny = Entry.Preview().IsShiny;
    }

    static V Back<V>(V now, V before, V after) => EqualityComparer<V>.Default.Equals(now, after) ? before : now;
}

/// <summary>The same change on many Pokémon, as pending drafts: each one is saved and checked like any other edit.</summary>
public sealed partial class SaveDocument
{
    readonly Stack<List<BatchChange>> batches = new();

    public bool CanUndoBatch => batches.Count != 0;

    public List<PokemonEntry> InScope(BatchScope scope, int box) => (scope switch {
        BatchScope.Team => Pokemon.Where(p => p.IsParty),
        BatchScope.OpenBox => Pokemon.Where(p => !p.IsParty && BoxOf(p) == box),
        BatchScope.AllBoxes => Pokemon.Where(p => !p.IsParty),
        _ => Pokemon,
    }).ToList();

    /// <summary>Prepares the plan on each Pokémon. What one of them can't take is skipped, and the rest of its changes stay.</summary>
    public BatchResult ApplyBatch(IReadOnlyCollection<PokemonEntry> chosen, BatchPlan plan)
    {
        if (plan.Problem(Rules) is { } problem) throw new InvalidOperationException(problem);
        var before = chosen.Select(e => e.Edit.Copy()).ToList();
        var skipped = new List<BatchSkip>();
        var steps = Steps(plan);
        try
        {
            foreach (var entry in chosen) Prepare(entry, steps, skipped);
            if (plan.RemoveItem) ReturnItems(chosen, skipped);
        }
        // All or nothing: half a batch couldn't be undone as one, and would be left with no record of it.
        catch
        {
            foreach (var (entry, draft) in chosen.Zip(before)) entry.Edit = draft;
            throw;
        }
        var changes = chosen.Zip(before).Where(x => !x.First.Edit.SameAs(x.Second)).Select(x => new BatchChange(x.First, x.Second, x.First.Edit.Copy())).ToList();
        if (changes.Count != 0) batches.Push(changes);
        return new(chosen.Count, changes.Count, skipped);
    }

    /// <summary>Takes the last batch back. Nothing changes if an item it took off can't return to its Pokémon any more.</summary>
    public string UndoBatch()
    {
        var changes = batches.Peek();
        var current = changes.Select(c => c.Entry.Edit).ToList();
        string? supply = ValidateSupply();
        foreach (var change in changes) change.Undo();
        if (ValidateSupply() is { } error && error != supply)
        {
            for (int i = 0; i < changes.Count; i++) changes[i].Entry.Edit = current[i];
            throw new InvalidOperationException(error);
        }
        batches.Pop();
        return T("Lote deshecho: {0} Pokémon.", changes.Count);
    }

    // Shiny goes before the IVs: on Game Boy it sets the DVs, and the value asked for has to live with them.
    List<BatchStep> Steps(BatchPlan plan)
    {
        var steps = new List<BatchStep>();
        if (plan.Shiny is { } shiny) steps.Add(new(T("Shiny"), e => SetShiny(e, shiny)));
        if (plan.IVs is { } iv) steps.Add(new(Rules.IVName, e => SetIVs(e, iv)));
        if (plan.EVs is { } evs) steps.Add(new(Rules.EVName, e => SetEVs(e, evs)));
        if (plan.Level is { } level) steps.Add(new(T("Nivel"), Hatched(e => e.Edit.Level = level)));
        if (plan.Nature is { } nature) steps.Add(new(T("Naturaleza"), e => {
            if (!Rules.HasNature) return T("Este juego no tiene naturalezas.");
            e.Edit.Nature = nature;
            return null;
        }));
        if (plan.MaxFriendship) steps.Add(new(T("Amistad"), Rules.HasFriendship ? Hatched(e => e.Edit.Friendship = BatchPlan.MaxFriendshipValue) : _ => T("Este juego no tiene amistad.")));
        if (plan.Ball is { } ball) steps.Add(new(T("Poké Ball"), Rules.HasBall ? Hatched(e => e.Edit.Ball = ball) : _ => T("Este juego no tiene Poké Balls que elegir.")));
        // Someone who never had a nickname keeps the name as the file has it, and so nothing is pending for them.
        if (plan.ClearNickname) steps.Add(new(T("Apodo"), Hatched(e => e.Edit.Nickname = e.Original.IsNicknamed ? "" : e.Baseline.Nickname)));
        if (plan.Trainer is { } trainer) steps.Add(new(T("Entrenador original"), Hatched(e => e.Edit.Trainer = trainer)));
        return steps;
    }

    // The editor locks these fields on an egg. Its friendship is really the count until it hatches.
    static Func<PokemonEntry, string?> Hatched(Action<PokemonEntry> change) => e => {
        if (e.Original.IsEgg) return T("Un huevo no admite este cambio.");
        change(e);
        return null;
    };

    static string? SetShiny(PokemonEntry entry, bool shiny)
    {
        if (!entry.Rules.HasShiny) return T("Este juego no tiene Pokémon shiny.");
        return entry.Edit.IsShiny == shiny ? null : KeepingLooks(entry, () => Catalog.SetShiny(entry, shiny));
    }

    static string? SetIVs(PokemonEntry entry, int value) => KeepingLooks(entry, () => {
        for (int stat = 0; stat < entry.Edit.IVs.Length; stat++)
            if (entry.Rules.IsStatEditable(stat, true)) entry.Edit.IVs[stat] = value;
    });

    static string? SetEVs(PokemonEntry entry, int[] values)
    {
        var d = entry.Edit;
        for (int stat = 0; stat < d.EVs.Length; stat++)
            if (entry.Rules.IsStatEditable(stat, false)) d.EVs[stat] = values[stat];
        if (entry.Rules.SharesSpecial) d.EVs[5] = d.EVs[4];
        return null;
    }

    // On Game Boy the DVs also decide shininess and Unown's letter. A value that would alter them by accident doesn't stay,
    // and one that does stay brings along the DVs the game works out from the others.
    static string? KeepingLooks(PokemonEntry entry, Action change)
    {
        var before = entry.Edit.Copy();
        change();
        if (!entry.Rules.IsGameBoy) return null;
        var preview = entry.Preview();
        if (preview.Form == entry.Edit.Form && (!entry.Rules.HasShiny || preview.IsShiny == entry.Edit.IsShiny))
        {
            preview.GetIVs(entry.Edit.IVs);
            return null;
        }
        entry.Edit = before;
        return T("En Game Boy sus DV deciden su aspecto, y este cambio lo alteraría.");
    }

    // Validating costs a list of moves, so a Pokémon is checked as a whole. Only one that fails goes step by step,
    // which also leaves it as it was if it can't be valid whatever is done: the save stays possible for the rest.
    static void Prepare(PokemonEntry entry, List<BatchStep> steps, List<BatchSkip> skipped)
    {
        var start = entry.Edit.Copy();
        var refused = steps.Select(step => (step, Reason: step.Run(entry))).Where(x => x.Reason != null).ToList();
        if (entry.Edit.SameAs(start) || Catalog.Validate(entry) == null)
        {
            skipped.AddRange(refused.Select(x => new BatchSkip(entry, x.step.Change, x.Reason!)));
            return;
        }
        entry.Edit = start;
        foreach (var step in steps)
        {
            var before = entry.Edit.Copy();
            var reason = step.Run(entry);
            if (reason == null && !entry.Edit.SameAs(before)) reason = Catalog.Validate(entry);
            if (reason == null) continue;
            entry.Edit = before;
            skipped.Add(new(entry, step.Change, reason));
        }
    }

    // All at once, because checking the bag is what takes time, and one by one only if they don't all fit.
    void ReturnItems(IEnumerable<PokemonEntry> chosen, List<BatchSkip> skipped)
    {
        var known = Items.Select(i => (int)i.Id).ToHashSet();
        var holders = new List<PokemonEntry>();
        foreach (var entry in chosen)
        {
            if (WhyKeepsItem(entry, known) is { } reason) skipped.Add(new(entry, T("Objeto"), reason));
            else if (entry.Edit.HeldItem != 0) holders.Add(entry);
        }
        var items = holders.Select(e => e.Edit.HeldItem).ToList();
        string? supply = ValidateSupply();
        foreach (var entry in holders) entry.Edit.HeldItem = 0;
        if (ValidateSupply() is not { } error || error == supply) return;
        for (int i = 0; i < holders.Count; i++) holders[i].Edit.HeldItem = items[i];
        foreach (var entry in holders)
        {
            try { EquipItem(entry, 0); }
            catch (InvalidOperationException ex) { skipped.Add(new(entry, T("Objeto"), ex.Message)); }
        }
    }

    string? WhyKeepsItem(PokemonEntry entry, HashSet<int> known)
    {
        int item = entry.Edit.HeldItem;
        if (!Rules.HasHeldItems) return T("Este juego no tiene objetos equipados.");
        if (item == 0) return null;
        if (Rules.IsMail(item)) return T("Lleva una carta. Quítasela en el juego.");
        if (Rules.ItemForForm(entry.Original.Species, entry.Edit.Form) == item) return T("Su forma depende de ese objeto.");
        return known.Contains(item) ? null : T("Ese objeto no tiene lugar en la bolsa.");
    }
}
