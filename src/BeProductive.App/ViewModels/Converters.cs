using Avalonia.Data.Converters;
using BeProductive.Core.Sessions;

namespace BeProductive.App.ViewModels;

public static class Converters
{
    public static readonly IValueConverter BlockLabel = new FuncValueConverter<BlockKind, string>(kind => $"{kind.Label()} blocks");

    public static readonly IValueConverter Minutes = new FuncValueConverter<int, string>(minutes => Formatting.Duration(TimeSpan.FromMinutes(minutes)));
}
