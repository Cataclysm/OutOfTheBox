// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Text.Json;
using OutOfTheBox.Domain.Mcp;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Domain.Runs;
using Microsoft.EntityFrameworkCore;

namespace OutOfTheBox.Infrastructure.Persistence;

/// <summary>
/// EF Core mapping for the Domain entities in <see cref="OutOfTheBox.Domain.Runs"/> and the git
/// credential tracking types in <see cref="OutOfTheBox.Domain.Repositories"/> - the one place that
/// knows how <see cref="Run"/>/<see cref="RunResourceSample"/>/<see cref="GitHostAuthorization"/>/
/// <see cref="GitHostCredentialHealth"/>/<see cref="RepositoryCredentialHealth"/> are actually
/// stored; those types themselves stay plain, framework-free data holders.
/// </summary>
public sealed class OutOfTheBoxDbContext(DbContextOptions<OutOfTheBoxDbContext> options) : DbContext(options)
{
    /// <summary>Every run of every kind, per specs/run-history.</summary>
    public DbSet<Run> Runs => Set<Run>();

    /// <summary>Every run's resource-usage time series, per specs/run-history.</summary>
    public DbSet<RunResourceSample> RunResourceSamples => Set<RunResourceSample>();

    /// <summary>Every host explicitly authorized via <c>authorize_git_host</c>/the dashboard's PAT prompt, per specs/mcp-git-credentials. Never the token itself.</summary>
    public DbSet<GitHostAuthorization> GitHostAuthorizations => Set<GitHostAuthorization>();

    /// <summary>Every host's observed authentication health, per specs/mcp-git-credentials' <c>list_authorized_git_hosts</c> health field. Not the source for the dashboard/<c>list_repositories</c> needs-credential marker - see <see cref="RepositoryCredentialHealth"/> for that.</summary>
    public DbSet<GitHostCredentialHealth> GitHostCredentialHealth => Set<GitHostCredentialHealth>();

    /// <summary>Every repository's own observed authentication health, per specs/repository-management's needs-credential tracking - scoped to the repository, not shared across every repository on the same host.</summary>
    public DbSet<RepositoryCredentialHealth> RepositoryCredentialHealth => Set<RepositoryCredentialHealth>();

    /// <summary>Every feed URL explicitly authorized via <c>authorize_nuget_feed</c>, per specs/mcp-nuget-credentials. The plaintext token is never stored here - only an Azure DevOps Artifacts feed's DPAPI-encrypted password (see <see cref="NuGetFeedAuthorization"/>'s remarks).</summary>
    public DbSet<NuGetFeedAuthorization> NuGetFeedAuthorizations => Set<NuGetFeedAuthorization>();

    /// <summary>Every MCP tool's/subcommand's current enabled state, per the MCP Settings dashboard page - see <see cref="McpToolPermissionEntry"/>.</summary>
    public DbSet<McpToolPermissionEntry> McpToolPermissions => Set<McpToolPermissionEntry>();

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

        // RepositoryPath is the same already-resolved absolute path Run.RepositoryPath uses (see
        // EfRunRepository) - matched as a plain key, no case normalization, since every write goes
        // through the one WorkingDirectoryResolver that always produces the same casing for the same
        // repository.
        modelBuilder.Entity<RepositoryCredentialHealth>().HasKey(h => h.RepositoryPath);

        // FeedUrl is stored as its canonical Uri.AbsoluteUri form (see NuGetFeedCredentialStore) and
        // used as the key directly.
        modelBuilder.Entity<NuGetFeedAuthorization>().HasKey(a => a.FeedUrl);

        // Key is either a bare tool name or "{executable}:{subcommand}" - see McpToolPermissionEntry's
        // own remarks - used as the primary key directly.
        modelBuilder.Entity<McpToolPermissionEntry>().HasKey(p => p.Key);
    }
}
