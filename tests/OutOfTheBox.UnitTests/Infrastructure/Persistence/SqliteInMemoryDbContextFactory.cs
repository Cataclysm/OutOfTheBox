// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OutOfTheBox.Infrastructure.Persistence;

namespace OutOfTheBox.UnitTests.Infrastructure.Persistence;

/// <summary>
/// A real SQLite <c>:memory:</c> connection (not EF Core's InMemory provider, which doesn't
/// exercise real SQL) kept open for the lifetime of the instance - a SQLite in-memory database is
/// discarded the moment its one connection closes, so the connection must outlive every
/// <see cref="OutOfTheBoxDbContext"/> created against it.
/// </summary>
public sealed class SqliteInMemoryDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OutOfTheBoxDbContext> _options;

    /// <summary>Opens the connection and creates the schema from the current EF Core model.</summary>
    public SqliteInMemoryDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<OutOfTheBoxDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var dbContext = CreateContext();
        dbContext.Database.EnsureCreated();
    }

    /// <summary>Creates a new <see cref="OutOfTheBoxDbContext"/> against the shared open connection.</summary>
    public OutOfTheBoxDbContext CreateContext() => new(_options);

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();
}
