using System.Text.Json;

namespace BeProductive.Core.Recovery;

public interface IRecoveryCodeStore
{
    RecoveryCodeState Load();

    void Save(RecoveryCodeState state);
}

public sealed class JsonRecoveryCodeStore(AppPaths paths) : IRecoveryCodeStore
{
    public RecoveryCodeState Load()
    {
        if (!File.Exists(paths.RecoveryCodesFile))
            return new RecoveryCodeState();

        return JsonSerializer.Deserialize<RecoveryCodeState>(File.ReadAllText(paths.RecoveryCodesFile), Json.Options)
            ?? throw new InvalidDataException($"{paths.RecoveryCodesFile} is empty or invalid.");
    }

    public void Save(RecoveryCodeState state) =>
        DurableFile.WriteAllText(paths.RecoveryCodesFile, JsonSerializer.Serialize(state, Json.Options));
}
