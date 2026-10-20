using BeProductive.Core.Sessions;

namespace BeProductive.Core.Tests;

public class RuleEditorTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    private readonly InMemorySessionStore store = new();
    private readonly RuleEditor editor;

    public RuleEditorTests()
    {
        store.Presets[0].AllowedApps = ["chrome.exe", "WINWORD.EXE"];
        store.Presets[0].BlockedDomains = ["reddit.com"];
        editor = new RuleEditor(store, new SessionOptions());
    }

    private Preset Uni => store.Find("uni")!;

    private static Preset Edit(Preset preset, Action<Preset> change)
    {
        var copy = new Preset
        {
            Id = preset.Id,
            Name = preset.Name,
            AllowedApps = [.. preset.AllowedApps],
            FolderShortcuts = [.. preset.FolderShortcuts],
            BlockedDomains = [.. preset.BlockedDomains],
            DefaultBlock = preset.DefaultBlock,
            AllowTerminal = preset.AllowTerminal,
            AllowElevatedTerminal = preset.AllowElevatedTerminal,
        };
        change(copy);
        return copy;
    }

    [Fact]
    public void Stricter_changes_apply_at_once()
    {
        var message = editor.SavePreset(Edit(Uni, preset =>
        {
            preset.AllowedApps.Remove("WINWORD.EXE");
            preset.BlockedDomains.Add("youtube.com");
            preset.Name = "University";
        }), Today);

        Assert.Equal("Saved.", message);
        Assert.Equal(["chrome.exe"], Uni.AllowedApps);
        Assert.Equal(["reddit.com", "youtube.com"], Uni.BlockedDomains);
        Assert.Equal("University", Uni.Name);
        Assert.Empty(store.PendingChanges());
    }

    [Fact]
    public void Looser_changes_wait_for_tomorrow_while_stricter_ones_apply_now()
    {
        var message = editor.SavePreset(Edit(Uni, preset =>
        {
            preset.AllowedApps.Add("Steam.exe");
            preset.AllowedApps.Remove("WINWORD.EXE");
            preset.BlockedDomains.Clear();
        }), Today);

        Assert.Equal("Saved. From tomorrow: allow Steam.exe, unblock reddit.com.", message);
        Assert.Equal(["chrome.exe"], Uni.AllowedApps);
        Assert.Equal(["reddit.com"], Uni.BlockedDomains);

        var pending = Assert.Single(store.PendingChanges());
        Assert.Equal(Today.AddDays(1), pending.EffectiveOn);

        editor.ApplyDue(Today);
        Assert.DoesNotContain("Steam.exe", Uni.AllowedApps);

        editor.ApplyDue(Today.AddDays(1));
        Assert.Equal(["chrome.exe", "Steam.exe"], Uni.AllowedApps);
        Assert.Empty(Uni.BlockedDomains);
        Assert.Empty(store.PendingChanges());
    }

    [Fact]
    public void Turning_terminals_on_waits_but_turning_them_off_is_immediate()
    {
        editor.SavePreset(Edit(Uni, preset => preset.AllowElevatedTerminal = true), Today);

        Assert.False(Uni.AllowTerminal);
        Assert.Contains("allow terminals, allow admin terminals", store.PendingChanges()[0].Description);

        editor.ApplyDue(Today.AddDays(1));
        Assert.True(Uni.AllowTerminal);
        Assert.True(Uni.AllowElevatedTerminal);

        editor.SavePreset(Edit(Uni, preset => preset.AllowElevatedTerminal = false), Today.AddDays(1));
        Assert.True(Uni.AllowTerminal);
        Assert.False(Uni.AllowElevatedTerminal);
    }

    [Fact]
    public void Taking_back_a_loosening_change_cancels_it()
    {
        editor.SavePreset(Edit(Uni, preset => preset.AllowedApps.Add("Steam.exe")), Today);
        Assert.Single(store.PendingChanges());

        editor.SavePreset(Edit(Uni, _ => { }), Today);

        Assert.Empty(store.PendingChanges());
    }

    [Fact]
    public void New_presets_can_be_picked_from_tomorrow()
    {
        var message = editor.SavePreset(new Preset { Name = "Reading & Notes" }, Today);

        Assert.Equal("\"Reading & Notes\" can be picked from tomorrow.", message);
        Assert.Null(store.Find("reading-notes"));

        editor.ApplyDue(Today.AddDays(1));
        Assert.Equal("Reading & Notes", store.Find("reading-notes")!.Name);
    }

    [Fact]
    public void New_preset_ids_never_clash()
    {
        editor.SavePreset(new Preset { Name = "Uni" }, Today);

        Assert.Equal("uni-2", store.PendingChanges()[0].Target);
    }

    [Fact]
    public void Invalid_presets_are_refused()
    {
        Assert.Throws<SessionRuleException>(() => editor.SavePreset(new Preset { Name = "  " }, Today));
        Assert.Throws<SessionRuleException>(() => editor.SavePreset(Edit(Uni, preset => preset.BlockedDomains.Add("not a domain")), Today));
    }

    [Fact]
    public void The_preset_in_use_today_cannot_be_deleted()
    {
        Assert.Throws<SessionRuleException>(() => editor.DeletePreset("uni", presetInUse: "uni"));

        editor.DeletePreset("uni", presetInUse: null);
        Assert.Null(store.Find("uni"));
    }

    [Fact]
    public void Raising_minimums_is_immediate_and_lowering_waits()
    {
        var (message, today) = editor.SaveMinimums(new WeekdayMinimums { Tuesday = 150, Sunday = 0 }, Today);

        Assert.Equal("Saved. From tomorrow: lower minimums: Sunday 0 min.", message);
        Assert.Equal(150, today);
        Assert.Equal(150, editor.Minimums.Tuesday);
        Assert.Equal(100, editor.Minimums.Sunday);

        editor.ApplyDue(Today.AddDays(1));
        Assert.Equal(0, editor.Minimums.Sunday);
    }

    [Fact]
    public void A_raised_minimum_counts_today_only_before_planning()
    {
        var harness = new EngineHarness();
        harness.Engine.RaiseTodaysMinimum(150);
        Assert.Equal(150, harness.State.MinimumMinutes);

        harness.Plan(150);
        harness.Engine.RaiseTodaysMinimum(200);
        Assert.Equal(150, harness.State.MinimumMinutes);
    }

    [Fact]
    public void A_free_day_stays_free_when_its_minimum_is_raised()
    {
        var harness = new EngineHarness(new SessionOptions { MinimumMinutes = new WeekdayMinimums { Tuesday = 0 } });

        harness.Engine.RaiseTodaysMinimum(100);

        Assert.Equal(Phase.Free, harness.State.Phase);
    }
}
