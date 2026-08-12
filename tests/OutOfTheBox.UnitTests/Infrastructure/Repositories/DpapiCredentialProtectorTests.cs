// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace OutOfTheBox.UnitTests.Infrastructure.Repositories;

public sealed class DpapiCredentialProtectorTests
{
    private readonly DpapiCredentialProtector _protector = new(NullLogger<DpapiCredentialProtector>.Instance);

    [Theory]
    [InlineData("s3cr3t-personal-access-token")]
    [InlineData("")]
    [InlineData("a token with spaces and 🔑 unicode")]
    public void TryDecrypt_reverses_Encrypt(string plaintext)
    {
        var ciphertext = _protector.Encrypt(plaintext);

        Assert.True(_protector.TryDecrypt(ciphertext, out var decrypted));
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypting_the_same_value_twice_does_not_produce_identical_ciphertext()
    {
        // DPAPI includes fresh entropy on every call - confirms this isn't accidentally a no-op/
        // deterministic transform that would defeat the point of encrypting at all.
        var first = _protector.Encrypt("s3cr3t-personal-access-token");
        var second = _protector.Encrypt("s3cr3t-personal-access-token");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TryDecrypt_returns_false_for_corrupted_ciphertext_instead_of_throwing()
    {
        // A real DPAPI CryptographicException (corrupted blob), not a mock - confirms the failure
        // path is a reported "false", not an unhandled exception, so a caller treats it as "needs
        // re-authorization" rather than crashing. (Cross-scope Unprotect calls were tried as an
        // alternative failure trigger and turned out not reliable enough to assert on - Windows can
        // successfully decrypt CurrentUser-protected data via a LocalMachine-scoped Unprotect call
        // within the same logon session, so that isn't a dependable failure case here.)
        var ciphertext = _protector.Encrypt("s3cr3t");
        ciphertext[^1] ^= 0xFF;

        Assert.False(_protector.TryDecrypt(ciphertext, out _));
    }
}
