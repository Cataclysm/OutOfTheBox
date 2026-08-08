// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System;
using Xunit;

namespace OutOfTheBox.Msi.CustomActions.Tests
{
    public class SecretResolutionTests
    {
        [Fact]
        public void Resolve_prefers_an_explicit_current_value_over_a_stored_one()
        {
            // The scenario the original implementation got backwards: an operator explicitly
            // overriding a secret during an upgrade (e.g. `msiexec ... BEARERTOKEN=NewToken`)
            // must not be silently discarded in favor of the value stored from a prior install.
            var generatorCalled = false;

            var result = SecretResolution.Resolve("ExplicitValue", "StoredValue", () =>
            {
                generatorCalled = true;
                return "Generated";
            });

            Assert.Equal("ExplicitValue", result);
            Assert.False(generatorCalled);
        }

        [Fact]
        public void Resolve_reuses_the_stored_value_when_nothing_was_explicitly_supplied()
        {
            // The upgrade case this whole mechanism exists for: no command-line override, but a
            // prior install already configured a secret - it must carry forward unchanged.
            var generatorCalled = false;

            var result = SecretResolution.Resolve(string.Empty, "StoredValue", () =>
            {
                generatorCalled = true;
                return "Generated";
            });

            Assert.Equal("StoredValue", result);
            Assert.False(generatorCalled);
        }

        [Fact]
        public void Resolve_generates_a_new_value_only_when_nothing_else_is_available()
        {
            // A genuinely fresh install with nothing supplied via the command line.
            var result = SecretResolution.Resolve(string.Empty, string.Empty, () => "Generated");

            Assert.Equal("Generated", result);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("", null)]
        public void Resolve_treats_null_and_empty_as_equivalent_absence(string current, string existing)
        {
            var result = SecretResolution.Resolve(current, existing, () => "Generated");

            Assert.Equal("Generated", result);
        }

        [Fact]
        public void GenerateToken_produces_the_requested_number_of_random_bytes()
        {
            var token = SecretResolution.GenerateToken(32);

            var decoded = Convert.FromBase64String(token);
            Assert.Equal(32, decoded.Length);
        }

        [Fact]
        public void GenerateToken_calls_produce_different_values()
        {
            var first = SecretResolution.GenerateToken(32);
            var second = SecretResolution.GenerateToken(32);

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void GeneratePassword_satisfies_windows_complexity_requirements()
        {
            var password = SecretResolution.GeneratePassword();

            Assert.Equal(28, password.Length);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.DoesNotContain(password, char.IsWhiteSpace);
        }

        [Fact]
        public void GeneratePassword_calls_produce_different_values()
        {
            var first = SecretResolution.GeneratePassword();
            var second = SecretResolution.GeneratePassword();

            Assert.NotEqual(first, second);
        }
    }
}
