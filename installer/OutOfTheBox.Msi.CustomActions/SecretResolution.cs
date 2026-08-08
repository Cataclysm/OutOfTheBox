// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System;
using System.Security.Cryptography;

namespace OutOfTheBox.Msi.CustomActions
{
    /// <summary>
    /// Pure secret-resolution logic behind <see cref="ResolveSecretsAction"/>, with no dependency
    /// on <c>Session</c>/WiX types, so the precedence rule (the part most worth getting exactly
    /// right - a bug here can silently rotate or lose a credential) is directly unit-testable.
    /// </summary>
    public static class SecretResolution
    {
        /// <summary>
        /// Resolves a secret property's value given its current value (non-empty only if supplied
        /// explicitly on the command line - this runs before any UI could have set it),
        /// the persisted value from a prior install (empty on a genuinely fresh install), and a
        /// generator used only when neither of those is available.
        ///
        /// Precedence: an explicit current value always wins (an operator explicitly overriding a
        /// secret during an upgrade must not be silently discarded in favor of the prior stored
        /// value); otherwise the persisted value wins (upgrades never silently rotate a credential
        /// out from under the operator); only generate a new one when nothing else is available.
        /// </summary>
        public static string Resolve(string current, string existing, Func<string> generate)
        {
            if (!string.IsNullOrEmpty(current))
            {
                return current;
            }

            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }

            return generate();
        }

        /// <summary>Generates a cryptographically random, base64-encoded token.</summary>
        public static string GenerateToken(int byteLength)
        {
            var bytes = new byte[byteLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Generates a random password satisfying Windows local-account complexity (at least one
        /// each of upper/lower/digit/symbol). Ambiguous-looking characters (I/l/O/0/1) are
        /// excluded purely so a password an operator ever has to read off a screen isn't
        /// needlessly error-prone to transcribe - this is never displayed in the dialog, but could
        /// end up in a log.
        /// </summary>
        public static string GeneratePassword()
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnopqrstuvwxyz";
            const string digits = "23456789";
            const string symbols = "!@#$%^&*-_=+";
            const string all = upper + lower + digits + symbols;

            using (var rng = RandomNumberGenerator.Create())
            {
                var chars = new char[28];
                chars[0] = RandomChar(rng, upper);
                chars[1] = RandomChar(rng, lower);
                chars[2] = RandomChar(rng, digits);
                chars[3] = RandomChar(rng, symbols);
                for (var i = 4; i < chars.Length; i++)
                {
                    chars[i] = RandomChar(rng, all);
                }

                // Fisher-Yates shuffle so the four guaranteed-category characters aren't always in
                // the same leading positions.
                for (var i = chars.Length - 1; i > 0; i--)
                {
                    var j = RandomInt(rng, i + 1);
                    var temp = chars[i];
                    chars[i] = chars[j];
                    chars[j] = temp;
                }

                return new string(chars);
            }
        }

        private static char RandomChar(RandomNumberGenerator rng, string pool) => pool[RandomInt(rng, pool.Length)];

        private static int RandomInt(RandomNumberGenerator rng, int exclusiveMax)
        {
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            return (int)(BitConverter.ToUInt32(bytes, 0) % (uint)exclusiveMax);
        }
    }
}
