using System.Text;
using System.Text.Json;
using BeProductive.Core.SafetyNet;

namespace BeProductive.Core.Sessions;

/// <summary>
/// Changes to presets and daily minimums. Anything that makes the rules stricter applies at once;
/// anything that loosens them only applies from the next day, so it can't be used to get out of today.
/// </summary>
public sealed class RuleEditor(IRulesStore store, SessionOptions options)
{
    public WeekdayMinimums Minimums => store.LoadMinimums() ?? options.MinimumMinutes;

    /// <summary>Saves an edited or new preset. Returns a message saying what applies when.</summary>
    public string SavePreset(Preset edit, DateOnly today)
    {
        var preset = Normalize(edit);
        var current = string.IsNullOrEmpty(preset.Id) ? null : store.Find(preset.Id);
        if (current is null)
        {
            // A new preset could allow anything, so it's a loosening change as a whole.
            if (string.IsNullOrEmpty(preset.Id))
                preset.Id = NewId(preset.Name);
            store.SavePending(new PendingChange(
                PendingChangeKinds.Preset, preset.Id, today.AddDays(1), $"new preset \"{preset.Name}\"", Serialize(preset)));
            return $"\"{preset.Name}\" can be picked from tomorrow.";
        }

        var tightened = Tightened(current, preset);
        store.SavePreset(tightened);

        var loosening = Loosening(tightened, preset);
        if (loosening.Count == 0)
        {
            store.DeletePending(PendingChangeKinds.Preset, preset.Id);
            return "Saved.";
        }

        var description = string.Join(", ", loosening);
        store.SavePending(new PendingChange(PendingChangeKinds.Preset, preset.Id, today.AddDays(1), description, Serialize(preset)));
        return $"Saved. From tomorrow: {description}.";
    }

    public string DeletePreset(string id, string? presetInUse)
    {
        if (id == presetInUse)
            throw new SessionRuleException("That preset is in use today. Delete it once you're done.");
        if (store.Find(id) is null && store.PendingChanges().All(change => change.Target != id))
            throw new SessionRuleException($"There's no preset called '{id}'.");

        store.DeletePreset(id);
        store.DeletePending(PendingChangeKinds.Preset, id);
        return "Deleted.";
    }

    /// <summary>Saves new daily minimums. Returns a message and the minimum that now applies today.</summary>
    public (string Message, int Today) SaveMinimums(WeekdayMinimums edit, DateOnly today)
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            if (edit.For(day) is < 0 or > 16 * 60)
                throw new SessionRuleException("Minimums must be between 0 and 960 minutes.");
        }

        var current = Minimums;
        var tightened = Map(day => Math.Max(current.For(day), edit.For(day)));
        store.SaveMinimums(tightened);

        var lowered = Enum.GetValues<DayOfWeek>()
            .Where(day => edit.For(day) < tightened.For(day))
            .Select(day => $"{day} {edit.For(day)} min")
            .ToList();
        var todayMinimum = tightened.For(today.DayOfWeek);
        if (lowered.Count == 0)
        {
            store.DeletePending(PendingChangeKinds.Minimums, "");
            return ("Saved.", todayMinimum);
        }

        var description = "lower minimums: " + string.Join(", ", lowered);
        store.SavePending(new PendingChange(PendingChangeKinds.Minimums, "", today.AddDays(1), description, Serialize(edit)));
        return ($"Saved. From tomorrow: {description}.", todayMinimum);

        WeekdayMinimums Map(Func<DayOfWeek, int> minutes) => new()
        {
            Monday = minutes(DayOfWeek.Monday),
            Tuesday = minutes(DayOfWeek.Tuesday),
            Wednesday = minutes(DayOfWeek.Wednesday),
            Thursday = minutes(DayOfWeek.Thursday),
            Friday = minutes(DayOfWeek.Friday),
            Saturday = minutes(DayOfWeek.Saturday),
            Sunday = minutes(DayOfWeek.Sunday),
        };
    }

    /// <summary>Applies the pending changes that are due. Call before a new day's rules are read.</summary>
    public void ApplyDue(DateOnly today)
    {
        foreach (var change in store.PendingChanges().Where(change => change.EffectiveOn <= today))
        {
            switch (change.Kind)
            {
                case PendingChangeKinds.Preset:
                    store.SavePreset(Deserialize<Preset>(change.Data));
                    break;
                case PendingChangeKinds.Minimums:
                    store.SaveMinimums(Deserialize<WeekdayMinimums>(change.Data));
                    break;
            }

            store.DeletePending(change.Kind, change.Target);
        }
    }

    /// <summary>Keeps every stricter part of <paramref name="edit"/> and nothing looser than <paramref name="current"/>.</summary>
    public static Preset Tightened(Preset current, Preset edit) => new()
    {
        Id = current.Id,
        Name = edit.Name,
        FolderShortcuts = edit.FolderShortcuts,
        DefaultBlock = edit.DefaultBlock,
        AllowedApps = current.AllowedApps.Where(app => edit.AllowedApps.Contains(app, StringComparer.OrdinalIgnoreCase)).ToList(),
        BlockedDomains = current.BlockedDomains.Union(edit.BlockedDomains, StringComparer.OrdinalIgnoreCase).ToList(),
        AllowTerminal = current.AllowTerminal && edit.AllowTerminal,
        AllowElevatedTerminal = current.AllowElevatedTerminal && edit.AllowElevatedTerminal,
    };

    /// <summary>What <paramref name="edit"/> allows that <paramref name="current"/> doesn't, in words.</summary>
    public static List<string> Loosening(Preset current, Preset edit)
    {
        var changes = new List<string>();
        changes.AddRange(edit.AllowedApps.Except(current.AllowedApps, StringComparer.OrdinalIgnoreCase).Select(app => $"allow {app}"));
        changes.AddRange(current.BlockedDomains.Except(edit.BlockedDomains, StringComparer.OrdinalIgnoreCase).Select(domain => $"unblock {domain}"));
        if (edit.AllowTerminal && !current.AllowTerminal)
            changes.Add("allow terminals");
        if (edit.AllowElevatedTerminal && !current.AllowElevatedTerminal)
            changes.Add("allow admin terminals");
        return changes;
    }

    private static Preset Normalize(Preset edit)
    {
        var name = edit.Name.Trim();
        if (name.Length == 0)
            throw new SessionRuleException("A preset needs a name.");

        var domains = Clean(edit.BlockedDomains).Select(domain => domain.ToLowerInvariant()).ToList();
        var invalid = domains.FirstOrDefault(domain => !HostsSection.IsValidDomain(domain));
        if (invalid is not null)
            throw new SessionRuleException($"'{invalid}' isn't a domain name like youtube.com.");

        return new Preset
        {
            Id = edit.Id.Trim(),
            Name = name,
            AllowedApps = Clean(edit.AllowedApps),
            FolderShortcuts = Clean(edit.FolderShortcuts),
            BlockedDomains = domains,
            DefaultBlock = edit.DefaultBlock,
            AllowTerminal = edit.AllowTerminal || edit.AllowElevatedTerminal,
            AllowElevatedTerminal = edit.AllowElevatedTerminal,
        };

        static List<string> Clean(IEnumerable<string> values) =>
            values.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string NewId(string name)
    {
        var slug = new StringBuilder();
        foreach (var character in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
                slug.Append(character);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }

        var baseId = slug.ToString().Trim('-') is { Length: > 0 } trimmed ? trimmed : "preset";
        var taken = store.All().Select(preset => preset.Id)
            .Concat(store.PendingChanges().Select(change => change.Target))
            .ToHashSet();
        var id = baseId;
        for (var n = 2; taken.Contains(id); n++)
            id = $"{baseId}-{n}";
        return id;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json.CompactOptions);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Json.CompactOptions) ?? throw new InvalidDataException("Empty pending change.");
}
