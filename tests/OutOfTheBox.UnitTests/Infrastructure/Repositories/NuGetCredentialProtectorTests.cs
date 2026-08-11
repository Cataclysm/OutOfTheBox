// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Infrastructure.Repositories;

namespace OutOfTheBox.UnitTests.Infrastructure.Repositories;

public sealed class NuGetCredentialProtectorTests
{
    [Theory]
    [InlineData("s3cr3t-personal-access-token")]
    [InlineData("")]
    [InlineData("a token with spaces and 🔑 unicode")]
    public void Decrypt_reverses_Encrypt(string plaintext)
    {
        var ciphertext = NuGetCredentialProtector.Encrypt(plaintext);

        Assert.Equal(plaintext, NuGetCredentialProtector.Decrypt(ciphertext));
    }

    [Fact]
    public void Encrypting_the_same_value_twice_does_not_produce_identical_ciphertext()
    {
        // DPAPI includes fresh entropy on every call - confirms this isn't accidentally a no-op/
        // deterministic transform that would defeat the point of encrypting at all.
        var first = NuGetCredentialProtector.Encrypt("s3cr3t-personal-access-token");
        var second = NuGetCredentialProtector.Encrypt("s3cr3t-personal-access-token");

        Assert.NotEqual(first, second);
    }
}
