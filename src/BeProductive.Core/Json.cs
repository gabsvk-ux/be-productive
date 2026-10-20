using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeProductive.Core;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The same, on one line: for the pipe protocol, where each message is a line.</summary>
    public static readonly JsonSerializerOptions CompactOptions = new(Options) { WriteIndented = false };
}
