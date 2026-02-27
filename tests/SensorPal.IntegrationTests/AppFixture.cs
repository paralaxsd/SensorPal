using Alba;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SensorPal.Server;
using SensorPal.Server.Storage;
using Xunit;

namespace SensorPal.IntegrationTests;

/// <summary>
/// Shared xunit fixture: spins up the SensorPal server with an in-memory SQLite
/// database. One instance is created per test class that uses IClassFixture&lt;AppFixture&gt;.
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    readonly string _connStr = $"Data Source=sensorpal_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    SqliteConnection? _keepAlive;

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Keep one connection open so the named in-memory SQLite DB survives
        // across the multiple connections that DbContextFactory creates.
        _keepAlive = new SqliteConnection(_connStr);
        _keepAlive.Open();

        Host = await AlbaHost.For<Program.EntryPoint>(x =>
        {
            x.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AudioConfig:StoragePath"] = Path.GetTempPath(),
                    ["DisableApiKey"] = "true",
                }));

            x.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextFactory<SensorPalDbContext>>();
                services.RemoveAll<DbContextOptions<SensorPalDbContext>>();
                services.AddDbContextFactory<SensorPalDbContext>(o =>
                    o.UseSqlite(_connStr));
            });
        });
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        _keepAlive?.Dispose();
    }
}
