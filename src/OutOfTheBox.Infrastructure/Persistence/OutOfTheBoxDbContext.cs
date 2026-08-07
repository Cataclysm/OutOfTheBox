// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.Domain.Runs;
using Microsoft.EntityFrameworkCore;

namespace OutOfTheBox.Infrastructure.Persistence;

/// <summary>
/// EF Core mapping for the Domain entities in <see cref="OutOfTheBox.Domain.Runs"/> - the one
/// place that knows how <see cref="Run"/>/<see cref="RunResourceSample"/> are actually stored;
/// those types themselves stay plain, framework-free data holders.
/// </summary>
public sealed class OutOfTheBoxDbContext(DbContextOptions<OutOfTheBoxDbContext> options) : DbContext(options)
{
    /// <summary>Every run of every kind, per specs/run-history.</summary>
    public DbSet<Run> Runs => Set<Run>();

    /// <summary>Every run's resource-usage time series, per specs/run-history.</summary>
    public DbSet<RunResourceSample> RunResourceSamples => Set<RunResourceSample>();

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
    }
}
