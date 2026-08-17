// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace OutOfTheBox.Infrastructure.Persistence;

/// <summary>
/// Add-if-new, overwrite-if-existing upsert by primary key - the same three-line shape every
/// credential store's authorize/record-outcome write repeated verbatim (see
/// <c>GitCredentialStore</c>/<c>NuGetFeedCredentialStore</c>), extracted once it had been copy-pasted
/// a fourth time. Does not call <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/> -
/// that stays the caller's own step, same as before.
/// </summary>
internal static class EfUpsert
{
    /// <summary>Adds <paramref name="updated"/> if <paramref name="existing"/> is <see langword="null"/>; otherwise overwrites the tracked entity's current values with it.</summary>
    public static void Save<TEntity>(DbContext dbContext, TEntity? existing, TEntity updated) where TEntity : class
    {
        if (existing is null)
        {
            dbContext.Set<TEntity>().Add(updated);
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(updated);
        }
    }
}
