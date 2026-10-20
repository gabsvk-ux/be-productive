using BeProductive.Core.Recovery;

namespace BeProductive.Core.Tests;

public class RecoveryCodeTests
{
    [Fact]
    public void Generated_codes_survive_formatting_and_normalizing()
    {
        var code = RecoveryCode.Generate();

        Assert.Equal(RecoveryCode.Length, code.Length);
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", RecoveryCode.Format(code));
        Assert.True(RecoveryCode.TryNormalize(RecoveryCode.Format(code), out var normalized));
        Assert.Equal(code, normalized);
    }

    [Fact]
    public void Normalizing_accepts_any_case_spacing_and_look_alikes()
    {
        Assert.True(RecoveryCode.TryNormalize(" ol23-4567 89ab-cdef ", out var normalized));
        Assert.Equal("0123456789ABCDEF", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123-4567-89AB")]
    [InlineData("0123-4567-89AB-CDEF-0")]
    [InlineData("UUUU-UUUU-UUUU-UUUU")]
    public void Normalizing_rejects_anything_that_is_not_a_code(string? input) =>
        Assert.False(RecoveryCode.TryNormalize(input, out _));

    [Fact]
    public void A_hash_matches_only_its_own_code()
    {
        var code = RecoveryCode.Generate();
        var stored = RecoveryCodeHasher.Hash(code);

        Assert.True(RecoveryCodeHasher.Matches(code, stored));
        Assert.False(RecoveryCodeHasher.Matches(RecoveryCode.Generate(), stored));
    }
}
