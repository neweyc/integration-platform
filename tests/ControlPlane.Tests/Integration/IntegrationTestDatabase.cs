using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ControlPlane.Tests.IntegrationTests;

internal sealed class IntegrationTestDatabase : IAsyncDisposable
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5433;Database=postgres;Username=devuser;Password=devpassword";

    private IntegrationTestDatabase(string adminConnectionString, string databaseName, string connectionString)
    {
        AdminConnectionString = adminConnectionString;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string AdminConnectionString { get; }
    public string DatabaseName { get; }
    public string ConnectionString { get; }

    public static async Task<IntegrationTestDatabase?> CreateAsync()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("INTEGRATION_TEST_CONNECTION")
            ?? DefaultConnectionString;

        await using var admin = new NpgsqlConnection(adminConnectionString);

        try
        {
            await admin.OpenAsync();
        }
        catch
        {
            return null;
        }

        var databaseName = $"integration_platform_test_{Guid.NewGuid():N}";
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName
        };

        var database = new IntegrationTestDatabase(adminConnectionString, databaseName, builder.ConnectionString);
        await database.MigrateAsync();
        return database;
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    public async Task MigrateAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();

        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)";
        await drop.ExecuteNonQueryAsync();
    }
}
