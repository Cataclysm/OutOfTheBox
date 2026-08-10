// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Domain.Runs;
using Microsoft.EntityFrameworkCore;

namespace OutOfTheBox.Infrastructure.Persistence;

/// <summary>
/// EF Core mapping for the Domain entities in <see cref="OutOfTheBox.Domain.Runs"/> and the git
/// credential tracking types in <see cref="OutOfTheBox.Domain.Repositories"/> - the one place that
/// knows how <see cref="Run"/>/<see cref="RunResourceSample"/>/<see cref="GitHostAuthorization"/>/
/// <see cref="GitHostCredentialHealth"/> are actually stored; those types themselves stay plain,
/// framework-free data holders.
/// </summary>
public sealed class OutOfTheBoxDbContext(DbContextOptions<OutOfTheBoxDbContext> options) : DbContext(options)
{
    /// <summary>Every run of every kind, per specs/run-history.</summary>
    public DbSet<Run> Runs => Set<Run>();

    /// <summary>Every run's resource-usage time series, per specs/run-history.</summary>
    public DbSet<RunResourceSample> RunResourceSamples => Set<RunResourceSample>();

    /// <summary>Every host explicitly authorized via <c>authorize_git_host</c>/the dashboard's PAT prompt, per specs/mcp-git-credentials. Never the token itself.</summary>
    public DbSet<GitHostAuthorization> GitHostAuthorizations => Set<GitHostAuthorization>();

    /// <summary>Every host's observed authentication health, per specs/repository-management's needs-credential tracking.</summary>
    public DbSet<GitHostCredentialHealth> GitHostCredentialHealth => Set<GitHostCredentialHealth>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Run>(run =>
        {
            run.HasKey(r => r.Id);
            run.Property(r => r.Kind).HasConversion<string>();
            run.Property(r => r.Outcome).HasConversion<string>();

            // Arguments is init-only on the Domain entity - never mutated in place after the
            // initial insert - so a plain converter (no custom ValueComparer) is sufficient; EF
            // Core's change tracking only needs to notice the *reference* changing on Update,
            // which it does by default.
            run.Property(r => r.Arguments).HasConversion(
                toDb => toDb == null ? null : JsonSerializer.Serialize(toDb, JsonSerializerOptions.Default),
                fromDb => fromDb == null ? null : JsonSerializer.Deserialize<List<string>>(fromDb, JsonSerializerOptions.Default));
        });

        modelBuilder.Entity<RunResourceSample>(sample =>
        {
            // The Domain entity deliberately has no Id property (design.md lists only RunId,
            // Timestamp, CpuPercent, RamBytes) - EF Core still needs a primary key to work with,
            // so this is a shadow property that exists only in the mapping, never on the type itself.
            sample.Property<long>("Id").ValueGeneratedOnAdd();
            sample.HasKey("Id");
            sample.HasIndex(s => new { s.RunId, s.Timestamp });
        });

        // Host is stored lower-invariant (see GitCredentialStore) and used as the key directly,
        // rather than relying on a provider-specific collation for case-insensitive matching.
        modelBuilder.Entity<GitHostAuthorization>().HasKey(a => a.Host);
        modelBuilder.Entity<GitHostCredentialHealth>().HasKey(h => h.Host);
    }
}
