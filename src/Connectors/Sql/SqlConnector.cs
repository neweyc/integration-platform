using System.Data.Common;
using Dapper;
using Serto.Sdk;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace Serto.Connectors.Sql;

/// <summary>
/// The relational database engine a <see cref="SqlConnector"/> talks to. The connector is
/// transport-level: it handles connection lifecycle, secrets, and logging the same way for every
/// engine — only the underlying ADO.NET provider differs.
/// </summary>
public enum SqlProvider
{
    SqlServer,
    PostgreSql,
    MySql,
    Oracle
}

public sealed class SqlConnector
{
    private readonly IIntegrationContext _context;
    private readonly string _connectionString;
    private readonly SqlProvider _provider;

    public SqlConnector(
        IIntegrationContext context,
        string connectionString,
        SqlProvider provider = SqlProvider.SqlServer)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("SqlConnector: the connection string is empty.", nameof(connectionString));

        // Validate the connection string up front so a malformed value fails clearly at construction
        // (and during `serto test`) instead of with an opaque error on the first query. Each provider
        // parses its own dialect, so validate with the matching builder.
        ValidateConnectionString(provider, connectionString);

        _context = context;
        _connectionString = connectionString;
        _provider = provider;
    }

    public SqlProvider Provider => _provider;

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default)
    {
        _context.Logger.LogInformation("Executing {Provider} SQL Query: {Sql}", _provider, sql);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            return await connection.QueryAsync<T>(new CommandDefinition(sql, PrepareParameters(param), cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "SQL Query failed");
            throw;
        }
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default)
    {
        _context.Logger.LogInformation("Executing {Provider} SQL Command: {Sql}", _provider, sql);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            return await connection.ExecuteAsync(new CommandDefinition(sql, PrepareParameters(param), cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "SQL Command failed");
            throw;
        }
    }

    // Oracle's ADO.NET command binds parameters by POSITION by default, so named placeholders like
    // ":InvoiceId" would silently bind in declaration order rather than by name — a classic foot-gun.
    // Wrap the parameters so the connector flips BindByName on the OracleCommand and authors can use
    // ":Name" placeholders with an anonymous object the same way they do for every other provider.
    private object? PrepareParameters(object? param) =>
        _provider == SqlProvider.Oracle && param is not null
            ? new OracleBindByNameParameters(param)
            : param;

    // One connection per call (no shared mutable state). Each provider's ADO.NET connection derives
    // from DbConnection, so Dapper and the open/dispose lifecycle are identical across engines.
    private DbConnection CreateConnection() => _provider switch
    {
        SqlProvider.SqlServer => new SqlConnection(_connectionString),
        SqlProvider.PostgreSql => new NpgsqlConnection(_connectionString),
        SqlProvider.MySql => new global::MySqlConnector.MySqlConnection(_connectionString),
        SqlProvider.Oracle => new OracleConnection(_connectionString),
        _ => throw new ArgumentOutOfRangeException(nameof(_provider), _provider, "Unknown SQL provider.")
    };

    private static void ValidateConnectionString(SqlProvider provider, string connectionString)
    {
        try
        {
            _ = CreateConnectionStringBuilder(provider, connectionString);
        }
        catch (Exception ex) when (ex is not ArgumentOutOfRangeException)
        {
            throw new ArgumentException(
                $"SqlConnector: the {provider} connection string is malformed ({ex.Message}).",
                nameof(connectionString));
        }
    }

    private static DbConnectionStringBuilder CreateConnectionStringBuilder(SqlProvider provider, string connectionString) =>
        provider switch
        {
            SqlProvider.SqlServer => new SqlConnectionStringBuilder(connectionString),
            SqlProvider.PostgreSql => new NpgsqlConnectionStringBuilder(connectionString),
            SqlProvider.MySql => new global::MySqlConnector.MySqlConnectionStringBuilder(connectionString),
            SqlProvider.Oracle => new OracleConnectionStringBuilder(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown SQL provider.")
        };
}

// Dapper parameter wrapper that turns on Oracle's BindByName before binding. Dapper hands the live
// IDbCommand to IDynamicParameters.AddParameters, which is the one place we can reach the command to
// set the flag. Accepts either a plain anonymous object or an existing Dapper parameter set.
internal sealed class OracleBindByNameParameters(object parameters) : SqlMapper.IDynamicParameters
{
    public void AddParameters(System.Data.IDbCommand command, SqlMapper.Identity identity)
    {
        if (command is OracleCommand oracleCommand)
            oracleCommand.BindByName = true;

        var inner = parameters as SqlMapper.IDynamicParameters ?? new DynamicParameters(parameters);
        inner.AddParameters(command, identity);
    }
}

public static class IntegrationContextExtensions
{
    /// <summary>
    /// Resolves a SQL connector whose connection string is stored in the named secret.
    /// Defaults to SQL Server; pass a <see cref="SqlProvider"/> for PostgreSQL, MySQL, or Oracle —
    /// or use the engine-specific helpers below for a clearer call site.
    /// </summary>
    public static SqlConnector SqlConnector(
        this IIntegrationContext context,
        string connectionStringSecretKey,
        SqlProvider provider = SqlProvider.SqlServer)
    {
        if (context.Secrets.TryGetValue(connectionStringSecretKey, out var connectionString))
        {
            return new SqlConnector(context, connectionString, provider);
        }

        throw new InvalidOperationException($"Secret '{connectionStringSecretKey}' not found for SQL connection string.");
    }

    public static SqlConnector SqlServerConnector(this IIntegrationContext context, string connectionStringSecretKey) =>
        context.SqlConnector(connectionStringSecretKey, SqlProvider.SqlServer);

    public static SqlConnector PostgresConnector(this IIntegrationContext context, string connectionStringSecretKey) =>
        context.SqlConnector(connectionStringSecretKey, SqlProvider.PostgreSql);

    public static SqlConnector MySqlConnector(this IIntegrationContext context, string connectionStringSecretKey) =>
        context.SqlConnector(connectionStringSecretKey, SqlProvider.MySql);

    public static SqlConnector OracleConnector(this IIntegrationContext context, string connectionStringSecretKey) =>
        context.SqlConnector(connectionStringSecretKey, SqlProvider.Oracle);
}
