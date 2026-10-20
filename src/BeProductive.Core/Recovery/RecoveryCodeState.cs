using System.Text.Json.Serialization;

namespace BeProductive.Core.Recovery;

/// <summary>What's stored on disk. It holds only hashes: no code can be read back from it.</summary>
public sealed class RecoveryCodeState
{
    public int Version { get; set; } = 1;

    /// <summary>The set your friend holds.</summary>
    public RecoveryCodeSet? Active { get; set; }

    /// <summary>
    /// A new set that is being (or failed to be) emailed. Its codes are accepted too, in case the email
    /// went out but the app stopped before it could confirm that. Using one of them makes it the active set.
    /// </summary>
    public RecoveryCodeSet? Pending { get; set; }
}

public sealed class RecoveryCodeSet
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; }

    public List<StoredCode> Codes { get; set; } = [];

    [JsonIgnore]
    public int Remaining => Codes.Count(code => code.UsedAt is null);
}

public sealed class StoredCode
{
    public required string Salt { get; set; }

    public required string Hash { get; set; }

    public int Iterations { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}
