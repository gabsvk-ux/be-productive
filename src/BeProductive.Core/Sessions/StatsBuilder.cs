namespace BeProductive.Core.Sessions;

/// <summary>Puts the tracking records for a range of days together, with today's progress counted live.</summary>
public sealed class StatsBuilder(ISessionStore store, IRulesStore rules, SessionOptions options, TimeZoneInfo zone)
{
    /// <param name="today">Today's live status, including the running block's focus.</param>
    public StatsSnapshot Build(SessionStatus today, int dayCount, DateTimeOffset now)
    {
        var to = today.Day;
        var from = to.AddDays(-(Math.Max(1, dayCount) - 1));
        var recorded = store.Days(from, to).ToDictionary(day => day.Day);

        var days = new List<DayStats>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day == to)
                days.Add(Live(today));
            else if (recorded.TryGetValue(day, out var record))
                days.Add(FromRecord(record));
            else
                days.Add(new DayStats(day, 0, 0, 0, 0, null));
        }

        var names = rules.All().ToDictionary(preset => preset.Id, preset => preset.Name);
        var totals = store.FocusByPreset(from, to).ToDictionary(total => total.PresetId);
        if (today.Block is { } running && today.PresetId is { } presetId)
        {
            totals.TryGetValue(presetId, out var sofar);
            totals[presetId] = (presetId, sofar.Focused + running.Focused, sofar.CompletedBlocks);
        }

        var categories = totals.Values
            .Select(total => new CategoryStats(
                total.PresetId, names.GetValueOrDefault(total.PresetId, total.PresetId), Minutes(total.Focused), total.CompletedBlocks))
            .Where(category => category.FocusedMinutes > 0 || category.Blocks > 0)
            .OrderByDescending(category => category.FocusedMinutes)
            .ToList();

        // Streaks look at every recorded day, not just the range on screen.
        var history = store.Days(DateOnly.MinValue, to.AddDays(-1)).Select(day => (day.Day, day.Outcome)).Append((to, today.Outcome));
        var (current, best) = Streaks.From(history, to);

        var since = SessionEngine.DayStart(from, options, zone);
        return new StatsSnapshot(
            from,
            to,
            days,
            categories,
            current,
            best,
            store.CountEvents(SessionEventKinds.Pause, since, now),
            store.CountEvents(SessionEventKinds.Postpone, since, now),
            store.CountEvents(SessionEventKinds.RecoveryExit, since, now));
    }

    private static DayStats Live(SessionStatus today) =>
        new(today.Day, today.MinimumMinutes, Goal(today.MinimumMinutes, today.BindingMinutes, today.ExtraMinutes),
            Minutes(today.FocusedToday), today.CompletedBlocks, today.Outcome);

    private static DayStats FromRecord(DayRecord record) =>
        new(record.Day, record.MinimumMinutes, Goal(record.MinimumMinutes, record.BindingMinutes, record.ExtraMinutes),
            Minutes(record.Focused), record.CompletedBlocks, record.Outcome);

    private static int Goal(int minimum, int binding, int extra) => Math.Max(minimum, binding) + extra;

    private static int Minutes(TimeSpan time) => (int)Math.Floor(time.TotalMinutes);
}
