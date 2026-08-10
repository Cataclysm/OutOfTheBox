// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Host.ServiceRegistration;

/// <summary>Registers the SQLite-backed run history/resource-sample persistence (EF Core).</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Adds <see cref="OutOfTheBoxDbContext"/> and its repository implementations.</summary>
    public static IServiceCollection AddPersistenceServices(this IServiceCollection services)
    {
        // Resolved lazily from IOptions<ServiceOptions> (bound in Program.cs) rather than read eagerly
        // off IConfiguration here - WebApplicationFactory-driven tests merge their in-memory config
        // overrides during builder.Build(), so an eager read at this point would see the pre-override
        // (empty) value and every SQLite connection would silently open a private, discarded, anonymous
        // database instead of the configured file.
        services.AddDbContext<OutOfTheBoxDbContext>((serviceProvider, options) =>
        {
            var sqliteFilePath = serviceProvider.GetRequiredService<IOptions<ServiceOptions>>().Value.SqliteFilePath;
            options.UseSqlite($"Data Source={sqliteFilePath}");
        });
        services.AddScoped<IRunRepository, EfRunRepository>();
        services.AddScoped<IRunResourceSampleRepository, EfRunResourceSampleRepository>();

        return services;
    }
}
