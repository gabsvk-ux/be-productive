using BeProductive.Core.Ipc;
using BeProductive.Core.Recovery;
using BeProductive.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BeProductive.Core.Tests;

public sealed class SessionServiceTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly InMemorySessionStore store = new();
    private readonly FakeTimeProvider time = new(EngineHarness.Tuesday9am);
    private readonly RecoveryCodeService codes;
    private readonly SessionService service;

    public SessionServiceTests()
    {
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        codes = new RecoveryCodeService(
            new JsonRecoveryCodeStore(directory.AppPaths),
            new FakeDelivery(),
            Options.Create(new RecoveryOptions()),
            time,
            NullLogger<RecoveryCodeService>.Instance);
        service = new SessionService(store, store, codes, Options.Create(new SessionOptions()), time, NullLogger<SessionService>.Instance);
    }

    public void Dispose() => directory.Dispose();

    [Fact]
    public async Task Commands_return_the_new_status_and_refusals_explain_why()
    {
        var refused = await service.HandleAsync(new PlanRequest("uni", 30, BlockKind.Classic25, null), default);
        Assert.False(refused.Ok);
        Assert.Contains("at least 100", refused.Error);

        var planned = await service.HandleAsync(new PlanRequest("uni", 100, BlockKind.Classic25, "Calculus"), default);
        Assert.True(planned.Ok);
        Assert.Equal(Phase.Focus, planned.Status!.Phase);
        Assert.Equal("Calculus", planned.Status.Block!.Note);
        Assert.Equal(Phase.Focus, store.State!.Phase);
    }

    [Fact]
    public async Task Time_passes_through_ticks()
    {
        await service.HandleAsync(new PlanRequest("uni", 100, BlockKind.Classic25, null), default);

        for (var i = 0; i < 25 * 60 / 5; i++)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await service.TickAsync(default);
        }

        Assert.Equal(Phase.Break, (await service.HandleAsync(new StatusRequest(), default)).Status!.Phase);
    }

    [Fact]
    public async Task A_recovery_code_ends_the_lock()
    {
        var firstSet = await codes.CreateFirstSetAsync(default);
        await service.HandleAsync(new PlanRequest("uni", 100, BlockKind.Classic25, null), default);

        var response = await service.HandleAsync(new RecoveryExitRequest(firstSet[0]), default);

        Assert.True(response.Ok);
        Assert.Equal(DayOutcome.Failed, response.Status!.Outcome);
        Assert.Contains("9 recovery code(s) left", response.Message);
    }

    [Fact]
    public async Task A_wrong_code_leaves_the_lock_in_place()
    {
        await codes.CreateFirstSetAsync(default);

        var response = await service.HandleAsync(new RecoveryExitRequest("0000-0000-0000-0000"), default);

        Assert.False(response.Ok);
        Assert.Equal(Phase.Gate, response.Status!.Phase);
    }

    [Fact]
    public async Task No_code_is_used_up_when_nothing_is_locked()
    {
        var firstSet = await codes.CreateFirstSetAsync(default);
        await service.HandleAsync(new PostponeRequest(time.GetUtcNow().AddHours(1)), default);

        var response = await service.HandleAsync(new RecoveryExitRequest(firstSet[0]), default);

        Assert.False(response.Ok);
        Assert.Equal(10, codes.GetStatus().Remaining);
    }
}
