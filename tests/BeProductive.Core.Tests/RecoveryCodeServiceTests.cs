using BeProductive.Core.Recovery;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BeProductive.Core.Tests;

public sealed class RecoveryCodeServiceTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly FakeDelivery delivery = new();

    public void Dispose() => directory.Dispose();

    private RecoveryCodeService CreateService(int codeCount = 10, int promptThreshold = 3) =>
        new(
            new JsonRecoveryCodeStore(directory.AppPaths),
            delivery,
            Options.Create(new RecoveryOptions { CodeCount = codeCount, PromptThreshold = promptThreshold }),
            new FakeTimeProvider(),
            NullLogger<RecoveryCodeService>.Instance);

    [Fact]
    public async Task The_first_set_is_returned_once_and_only_hashes_are_stored()
    {
        var codes = await CreateService().CreateFirstSetAsync(default);

        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct().Count());
        var stored = await File.ReadAllTextAsync(directory.AppPaths.RecoveryCodesFile);
        foreach (var code in codes)
        {
            Assert.DoesNotContain(code, stored);
            Assert.DoesNotContain(code.Replace("-", ""), stored);
        }
    }

    [Fact]
    public async Task A_first_set_is_refused_while_unused_codes_exist()
    {
        var service = CreateService();
        await service.CreateFirstSetAsync(default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateFirstSetAsync(default));
    }

    [Fact]
    public async Task Each_code_works_exactly_once()
    {
        var service = CreateService();
        var codes = await service.CreateFirstSetAsync(default);

        var first = await service.RedeemAsync(codes[0], default);
        var again = await service.RedeemAsync(codes[0], default);

        Assert.Equal(RedeemOutcome.Accepted, first.Outcome);
        Assert.Equal(9, first.Remaining);
        Assert.Equal(RedeemOutcome.Rejected, again.Outcome);
    }

    [Fact]
    public async Task Unknown_codes_are_rejected()
    {
        var service = CreateService();
        await service.CreateFirstSetAsync(default);

        Assert.Equal(RedeemOutcome.Rejected, (await service.RedeemAsync(RecoveryCode.Format(RecoveryCode.Generate()), default)).Outcome);
        Assert.Equal(RedeemOutcome.Rejected, (await service.RedeemAsync("not a code", default)).Outcome);
    }

    [Fact]
    public async Task Redeeming_without_codes_says_so()
    {
        var result = await CreateService().RedeemAsync(RecoveryCode.Generate(), default);

        Assert.Equal(RedeemOutcome.NoCodes, result.Outcome);
    }

    [Fact]
    public async Task A_new_set_is_offered_once_few_codes_are_left()
    {
        var service = CreateService(codeCount: 5, promptThreshold: 3);
        var codes = await service.CreateFirstSetAsync(default);

        Assert.False((await service.RedeemAsync(codes[0], default)).ShouldOfferNewSet);
        Assert.True((await service.RedeemAsync(codes[1], default)).ShouldOfferNewSet);
    }

    [Fact]
    public async Task Using_the_last_code_sends_a_new_set_that_replaces_the_old_one()
    {
        var service = CreateService(codeCount: 2);
        var oldCodes = await service.CreateFirstSetAsync(default);

        await service.RedeemAsync(oldCodes[0], default);
        var last = await service.RedeemAsync(oldCodes[1], default);

        Assert.True(last.NewSetSent);
        var newCodes = Assert.Single(delivery.Sent);
        Assert.Equal(2, newCodes.Count);
        Assert.Equal(2, service.GetStatus().Remaining);
        Assert.Equal(RedeemOutcome.Accepted, (await service.RedeemAsync(newCodes[0], default)).Outcome);
    }

    [Fact]
    public async Task When_sending_after_the_last_code_fails_the_error_is_reported()
    {
        var service = CreateService(codeCount: 1);
        var codes = await service.CreateFirstSetAsync(default);
        delivery.Fail = true;

        var result = await service.RedeemAsync(codes[0], default);

        Assert.Equal(RedeemOutcome.Accepted, result.Outcome);
        Assert.False(result.NewSetSent);
        Assert.NotNull(result.NewSetError);
        Assert.Equal(0, service.GetStatus().Remaining);
    }

    [Fact]
    public async Task Old_codes_stay_valid_when_a_new_set_cannot_be_sent()
    {
        var service = CreateService(codeCount: 3);
        var codes = await service.CreateFirstSetAsync(default);
        delivery.Fail = true;

        await Assert.ThrowsAsync<DeliveryNotSentException>(() => service.RegenerateAsync(default));

        Assert.False(service.GetStatus().DeliveryPending);
        Assert.Equal(RedeemOutcome.Accepted, (await service.RedeemAsync(codes[0], default)).Outcome);
    }

    [Fact]
    public async Task Codes_that_reached_the_friend_work_even_if_sending_was_never_confirmed()
    {
        var service = CreateService(codeCount: 3);
        var oldCodes = await service.CreateFirstSetAsync(default);
        delivery.FailAfterSending = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RegenerateAsync(default));
        Assert.True(service.GetStatus().DeliveryPending);

        var result = await service.RedeemAsync(delivery.Sent[0][0], default);

        Assert.Equal(RedeemOutcome.Accepted, result.Outcome);
        Assert.False(service.GetStatus().DeliveryPending);
        Assert.Equal(2, result.Remaining);
        Assert.Equal(RedeemOutcome.Rejected, (await service.RedeemAsync(oldCodes[0], default)).Outcome);
    }
}
