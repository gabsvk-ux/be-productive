using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeProductive.Core.SafetyNet;

/// <summary>
/// Every change the app makes to the machine, recorded before it's made. Undoing the entries in reverse
/// order puts the machine back exactly as it was. restore.ps1 reads this file too, so keep its format stable.
/// </summary>
public sealed class ChangeJournal
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// When the applied changes must be undone if nothing renews them,
    /// so a crashed or confused session can't leave the machine locked.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public List<JournalEntry> Entries { get; set; } = [];
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RegistryValueEntry), "registryValue")]
[JsonDerivedType(typeof(RegistryKeyCreatedEntry), "registryKeyCreated")]
[JsonDerivedType(typeof(HostsSectionEntry), "hostsSection")]
public abstract record JournalEntry;

/// <summary>A registry value was set. Holds what was there before.</summary>
public sealed record RegistryValueEntry(
    string KeyPath,
    string Name,
    bool OriginalExists,
    PolicyValueKind? OriginalKind,
    string? OriginalData) : JournalEntry;

/// <summary>A registry key didn't exist and was created. Undoing deletes it if it's empty.</summary>
public sealed record RegistryKeyCreatedEntry(string KeyPath) : JournalEntry;

/// <summary>The app's section was added to the hosts file. Undoing removes it.</summary>
public sealed record HostsSectionEntry : JournalEntry;

public interface IJournalStore
{
    ChangeJournal Load();

    void Save(ChangeJournal journal);
}

public sealed class JsonJournalStore(AppPaths paths) : IJournalStore
{
    public ChangeJournal Load()
    {
        if (!File.Exists(paths.JournalFile))
            return new ChangeJournal();

        return JsonSerializer.Deserialize<ChangeJournal>(File.ReadAllText(paths.JournalFile), Json.Options)
            ?? throw new InvalidDataException($"{paths.JournalFile} is empty or invalid.");
    }

    public void Save(ChangeJournal journal)
    {
        if (journal.Entries.Count == 0)
        {
            File.Delete(paths.JournalFile);
            return;
        }

        DurableFile.WriteAllText(paths.JournalFile, JsonSerializer.Serialize(journal, Json.Options));
    }
}
