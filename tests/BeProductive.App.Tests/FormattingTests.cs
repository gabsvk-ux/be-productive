using BeProductive.App.ViewModels;
using BeProductive.Core.Sessions;

namespace BeProductive.App.Tests;

public class FormattingTests
{
    [Theory]
    [InlineData(100, BlockKind.Classic25, 115)] // 4 blocks, 3 short breaks
    [InlineData(150, BlockKind.Classic25, 190)] // 6 blocks, 4 short breaks + 1 long one
    [InlineData(100, BlockKind.Long45, 165)] // 3 blocks (135 min), 2 breaks of 15
    [InlineData(15, BlockKind.Short15, 15)]
    public void Estimates_include_breaks(int planned, BlockKind kind, int expectedMinutes) =>
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), Formatting.EstimateWithBreaks(planned, kind));

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(762, "12:42")]
    [InlineData(3725, "1:02:05")]
    [InlineData(-5, "00:00")]
    public void Clocks_count_minutes_and_seconds(int seconds, string expected) =>
        Assert.Equal(expected, Formatting.Clock(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(45, "45 min")]
    [InlineData(120, "2 h")]
    [InlineData(150, "2 h 30 min")]
    public void Durations_read_naturally(int minutes, string expected) =>
        Assert.Equal(expected, Formatting.Duration(TimeSpan.FromMinutes(minutes)));
}
