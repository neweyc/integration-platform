using System.Data;
using Dapper;
using Serto.Sdk;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Serto.Connectors.Sql;

public sealed class SqlConnector
{
    private readonly IIntegrationContext _context;
    private readonly string _connectionString;

    public SqlConnector(IIntegrationContext context, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("SqlConnector: the connection string is empty.", nameof(connectionString));

        // Validate the connection string up front so a malformed value fails clearly at construction
        // (and during `serto test`) instead of with an opaque error on the first query.
        try
        {
            _ = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"SqlConnector: the connection string is malformed ({ex.Message}).", nameof(connectionString));
        }

        _context = context;
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default)
    {
        _context.Logger.LogInformation("Executing SQL Query: {Sql}", sql);
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            return await connection.QueryAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "SQL Query failed");
            throw;
        }
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default)
    {
        _context.Logger.LogInformation("Executing SQL Command: {Sql}", sql);
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            return await connection.ExecuteAsync(new CommandDefinition(sql, param, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "SQL Command failed");
            throw;
        }
    }
}

public static class IntegrationContextExtensions
{
    public static SqlConnector SqlConnector(this IIntegrationContext context, string connectionStringSecretKey)
    {
        if (context.Secrets.TryGetValue(connectionStringSecretKey, out var connectionString))
        {
            return new SqlConnector(context, connectionString);
        }

        throw new InvalidOperationException($"Secret '{connectionStringSecretKey}' not found for SQL connection string.");
    }
}
