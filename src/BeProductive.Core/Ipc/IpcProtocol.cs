using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BeProductive.Core.Sessions;

namespace BeProductive.Core.Ipc;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(StatusRequest), "status")]
[JsonDerivedType(typeof(PresetsRequest), "presets")]
[JsonDerivedType(typeof(PlanRequest), "plan")]
[JsonDerivedType(typeof(PostponeRequest), "postpone")]
[JsonDerivedType(typeof(PauseRequest), "pause")]
[JsonDerivedType(typeof(ResumeRequest), "resume")]
[JsonDerivedType(typeof(StartNextRequest), "start-next")]
[JsonDerivedType(typeof(SwitchPresetRequest), "switch-preset")]
[JsonDerivedType(typeof(ExtendRequest), "extend")]
[JsonDerivedType(typeof(StopExtraRequest), "stop-extra")]
[JsonDerivedType(typeof(RecoveryExitRequest), "recovery-exit")]
[JsonDerivedType(typeof(SettingsRequest), "settings")]
[JsonDerivedType(typeof(SavePresetRequest), "save-preset")]
[JsonDerivedType(typeof(DeletePresetRequest), "delete-preset")]
[JsonDerivedType(typeof(SaveMinimumsRequest), "save-minimums")]
public abstract record IpcRequest;

public sealed record StatusRequest : IpcRequest;

public sealed record PresetsRequest : IpcRequest;

public sealed record PlanRequest(string PresetId, int Minutes, BlockKind Block, string? Note) : IpcRequest;

public sealed record PostponeRequest(DateTimeOffset Until) : IpcRequest;

public sealed record PauseRequest : IpcRequest;

public sealed record ResumeRequest : IpcRequest;

public sealed record StartNextRequest(BlockKind Block, string? Note) : IpcRequest;

public sealed record SwitchPresetRequest(string PresetId) : IpcRequest;

public sealed record ExtendRequest(int Minutes, BlockKind Block, string? PresetId, string? Note) : IpcRequest;

public sealed record StopExtraRequest : IpcRequest;

public sealed record RecoveryExitRequest(string Code) : IpcRequest;

public sealed record SettingsRequest : IpcRequest;

/// <summary>Saves a preset; an empty id creates a new one.</summary>
public sealed record SavePresetRequest(Preset Preset) : IpcRequest;

public sealed record DeletePresetRequest(string PresetId) : IpcRequest;

public sealed record SaveMinimumsRequest(WeekdayMinimums Minimums) : IpcRequest;

/// <summary>The rules in force now, and the loosening changes waiting for the next day.</summary>
public sealed record SettingsSnapshot(
    IReadOnlyList<Preset> Presets,
    WeekdayMinimums Minimums,
    IReadOnlyList<PendingRuleChange> Pending);

/// <param name="Preset">For a preset change: the preset as it will be.</param>
/// <param name="Minimums">For a minimums change: the minimums as they will be.</param>
public sealed record PendingRuleChange(
    string Kind,
    string Target,
    DateOnly EffectiveOn,
    string Description,
    Preset? Preset,
    WeekdayMinimums? Minimums);

/// <param name="Error">Why the command was refused, ready to show to the user.</param>
/// <param name="Message">Anything else worth telling the user, such as how many recovery codes are left.</param>
public sealed record IpcResponse(
    bool Ok,
    string? Error,
    SessionStatus? Status,
    IReadOnlyList<Preset>? Presets = null,
    string? Message = null,
    SettingsSnapshot? Settings = null);

public sealed class ServiceUnavailableException(string message) : Exception(message);

/// <summary>One JSON request line in, one JSON response line out, over a local named pipe.</summary>
public static class IpcProtocol
{
    public const string PipeName = "BeProductive";

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json.CompactOptions);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json.CompactOptions);

    public static async Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(3_000, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new ServiceUnavailableException("The Be Productive service isn't running.");
        }

        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(Serialize(request));

        var line = await reader.ReadLineAsync(cancellationToken)
            ?? throw new ServiceUnavailableException("The service closed the connection without answering.");
        return Deserialize<IpcResponse>(line)
            ?? throw new InvalidDataException("The service sent an empty answer.");
    }
}
