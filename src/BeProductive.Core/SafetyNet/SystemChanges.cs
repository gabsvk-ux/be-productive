using BeProductive.Core.Enforcement;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BeProductive.Core.SafetyNet;

public sealed class SafetyNetOptions
{
    public const string SectionName = "SafetyNet";

    /// <summary>How long applied changes last if nothing renews them.</summary>
    public TimeSpan DefaultLease { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How often the service checks for an expired lease or the panic file.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed record AppliedChanges(int Count, DateTimeOffset? ExpiresAt);

public sealed record RestoreReport(int Restored, int Failed);

/// <summary>
/// The only way the app changes the machine. Each change is written to the journal before it's made
/// (so even a crash halfway through can be undone), and <see cref="RestoreAll"/> undoes all of them.
/// </summary>
public sealed class SystemChanges(
    IJournalStore journalStore,
    IRegistry registry,
    IHostsFile hostsFile,
    IDnsCache dnsCache,
    EnforcementGate gate,
    IOptions<SafetyNetOptions> options,
    TimeProvider time,
    ILogger<SystemChanges> logger)
{
    private readonly object sync = new();

    public AppliedChanges Describe()
    {
        lock (sync)
        {
            var journal = journalStore.Load();
            return new AppliedChanges(journal.Entries.Count, journal.ExpiresAt);
        }
    }

    public bool SetRegistryValue(string keyPath, string name, PolicyValueKind kind, string data) =>
        gate.TryEnforce($@"set {keyPath}\{name} = {data}", () =>
        {
            lock (sync)
            {
                var journal = journalStore.Load();
                var alreadyRecorded = journal.Entries.OfType<RegistryValueEntry>().Any(entry =>
                    string.Equals(entry.KeyPath, keyPath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

                // Only the first change records the original; later ones would record our own value.
                if (!alreadyRecorded)
                {
                    var original = registry.ReadValue(keyPath, name);
                    if (original.Kind == PolicyValueKind.Unsupported)
                        throw new InvalidOperationException($@"{keyPath}\{name} already holds a value of a kind that can't be restored, so it's left alone.");

                    RecordMissingKeys(journal, keyPath);
                    journal.Entries.Add(new RegistryValueEntry(keyPath, name, original.Exists, original.Kind, original.Data));
                    StartLease(journal);
                    journalStore.Save(journal);
                }

                registry.CreateKey(keyPath);
                registry.WriteValue(keyPath, name, kind, data);
            }
        });

    public bool SetHostsBlocklist(IReadOnlyCollection<string> domains) =>
        gate.TryEnforce($"block {domains.Count} domain(s) in the hosts file", () =>
        {
            lock (sync)
            {
                var journal = journalStore.Load();
                if (!journal.Entries.OfType<HostsSectionEntry>().Any())
                {
                    journal.Entries.Add(new HostsSectionEntry());
                    StartLease(journal);
                    journalStore.Save(journal);
                }

                hostsFile.Write(HostsSection.Apply(hostsFile.Read(), domains));
                dnsCache.Flush();
            }
        });

    /// <summary>Keeps the applied changes in place until <paramref name="expiresAt"/>.</summary>
    public void RenewLease(DateTimeOffset expiresAt)
    {
        lock (sync)
        {
            var journal = journalStore.Load();
            if (journal.Entries.Count == 0)
                return;
            journal.ExpiresAt = expiresAt;
            journalStore.Save(journal);
        }
    }

    public RestoreReport RestoreIfExpired()
    {
        lock (sync)
        {
            var journal = journalStore.Load();
            if (journal.Entries.Count == 0 || journal.ExpiresAt > time.GetUtcNow())
                return new RestoreReport(0, 0);
            return Restore(journal, "the lease expired");
        }
    }

    /// <summary>
    /// Undoes every recorded change, newest first. This always runs, whatever the enforcement mode:
    /// undoing is always safe. Entries that fail to undo stay in the journal to be retried.
    /// </summary>
    public RestoreReport RestoreAll(string reason)
    {
        lock (sync)
        {
            var journal = journalStore.Load();
            return journal.Entries.Count == 0 ? new RestoreReport(0, 0) : Restore(journal, reason);
        }
    }

    private RestoreReport Restore(ChangeJournal journal, string reason)
    {
        logger.LogWarning("Undoing {Count} change(s) because {Reason}", journal.Entries.Count, reason);

        var failed = new List<JournalEntry>();
        var restored = 0;
        var hostsChanged = false;
        for (var i = journal.Entries.Count - 1; i >= 0; i--)
        {
            var entry = journal.Entries[i];
            try
            {
                Undo(entry);
                restored++;
                hostsChanged |= entry is HostsSectionEntry;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Couldn't undo {Entry}; it stays in the journal to retry", entry);
                failed.Insert(0, entry);
            }
        }

        if (hostsChanged)
        {
            try
            {
                dnsCache.Flush();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Couldn't flush the DNS cache after restoring the hosts file");
            }
        }

        journal.Entries = failed;
        if (failed.Count == 0)
            journal.ExpiresAt = null;
        journalStore.Save(journal);
        return new RestoreReport(restored, failed.Count);
    }

    private void Undo(JournalEntry entry)
    {
        switch (entry)
        {
            case RegistryValueEntry { OriginalExists: true } value:
                registry.CreateKey(value.KeyPath);
                registry.WriteValue(value.KeyPath, value.Name, value.OriginalKind!.Value, value.OriginalData!);
                break;
            case RegistryValueEntry value:
                registry.DeleteValue(value.KeyPath, value.Name);
                break;
            case RegistryKeyCreatedEntry key:
                registry.DeleteKeyIfEmpty(key.KeyPath);
                break;
            case HostsSectionEntry:
                var content = hostsFile.Read();
                var restored = HostsSection.Remove(content);
                if (restored != content)
                    hostsFile.Write(restored);
                break;
            default:
                throw new NotSupportedException($"Unknown journal entry {entry.GetType().Name}.");
        }
    }

    /// <summary>Records each missing level of <paramref name="keyPath"/>, outermost first, so undo deletes the innermost first.</summary>
    private void RecordMissingKeys(ChangeJournal journal, string keyPath)
    {
        var parts = keyPath.Split('\\');
        for (var depth = 2; depth <= parts.Length; depth++)
        {
            var path = string.Join('\\', parts[..depth]);
            var alreadyRecorded = journal.Entries.OfType<RegistryKeyCreatedEntry>()
                .Any(entry => string.Equals(entry.KeyPath, path, StringComparison.OrdinalIgnoreCase));
            if (!alreadyRecorded && !registry.KeyExists(path))
                journal.Entries.Add(new RegistryKeyCreatedEntry(path));
        }
    }

    private void StartLease(ChangeJournal journal) =>
        journal.ExpiresAt ??= time.GetUtcNow() + options.Value.DefaultLease;
}
