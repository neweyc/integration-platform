using Serto.Connectors.Sql;
using Serto.Testing;

namespace Connectors.Tests;

public class SqlConnectorTests
{
    private static TestIntegrationContext Context() => new();

    private static TestIntegrationContext ContextWithSecret(string key, string value) =>
        new() { Secrets = new Dictionary<string, string> { [key] = value } };

    // Valid (well-formed) connection strings for each engine. Construction validates the string but
    // does not open a connection, so these need no live database.
    public static TheoryData<SqlProvider, string> ValidConnectionStrings => new()
    {
        { SqlProvider.SqlServer, "Server=localhost;Database=app;User Id=sa;Password=secret;" },
        { SqlProvider.PostgreSql, "Host=localhost;Database=app;Username=postgres;Password=secret" },
        { SqlProvider.MySql, "Server=localhost;Database=app;User ID=root;Password=secret" },
        { SqlProvider.Oracle, "Data Source=localhost:1521/XEPDB1;User Id=system;Password=secret" }
    };

    [Theory]
    [MemberData(nameof(ValidConnectionStrings))]
    public void Constructor_ValidConnectionString_Succeeds_PerProvider(SqlProvider provider, string connectionString)
    {
        var connector = new SqlConnector(Context(), connectionString, provider);

        Assert.Equal(provider, connector.Provider);
    }

    [Fact]
    public void Constructor_DefaultsToSqlServer()
    {
        var connector = new SqlConnector(Context(), "Server=localhost;Database=app;User Id=sa;Password=secret;");

        Assert.Equal(SqlProvider.SqlServer, connector.Provider);
    }

    [Theory]
    [InlineData(SqlProvider.SqlServer)]
    [InlineData(SqlProvider.PostgreSql)]
    public void Constructor_MalformedConnectionString_Throws_WithProviderName(SqlProvider provider)
    {
        // A common mistake: a non-connection-string value (e.g. an API key) put in the SQL secret.
        var exception = Assert.Throws<ArgumentException>(() =>
            new SqlConnector(Context(), "not-a-real-connection-string", provider));

        Assert.Contains("malformed", exception.Message, StringComparison.Ordinal);
        Assert.Contains(provider.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_EmptyConnectionString_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SqlConnector(Context(), "   "));
    }

    [Fact]
    public void Extension_MissingSecret_ThrowsClearError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Context().SqlConnector("MISSING_CONN"));

        Assert.Contains("MISSING_CONN", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Extension_SqlConnector_DefaultsToSqlServer()
    {
        var context = ContextWithSecret("DB", "Server=localhost;Database=app;User Id=sa;Password=secret;");

        Assert.Equal(SqlProvider.SqlServer, context.SqlConnector("DB").Provider);
    }

    [Fact]
    public void Extension_PostgresConnector_SelectsPostgreSql()
    {
        var context = ContextWithSecret("PG", "Host=localhost;Database=app;Username=postgres;Password=secret");

        Assert.Equal(SqlProvider.PostgreSql, context.PostgresConnector("PG").Provider);
    }

    [Fact]
    public void Extension_OracleConnector_SelectsOracle()
    {
        var context = ContextWithSecret("ORA", "Data Source=localhost:1521/XEPDB1;User Id=system;Password=secret");

        Assert.Equal(SqlProvider.Oracle, context.OracleConnector("ORA").Provider);
    }

    [Fact]
    public void Extension_MySqlConnector_SelectsMySql()
    {
        var context = ContextWithSecret("MY", "Server=localhost;Database=app;User ID=root;Password=secret");

        Assert.Equal(SqlProvider.MySql, context.MySqlConnector("MY").Provider);
    }

    [Fact]
    public void OracleBindByNameParameters_SetsBindByName_AndBindsParameters()
    {
        // Oracle binds by position unless BindByName is set. Prove the wrapper flips it and still
        // hands the named parameters to the command — no live database needed.
        using var connection = new Oracle.ManagedDataAccess.Client.OracleConnection();
        using var command = new Oracle.ManagedDataAccess.Client.OracleCommand
        {
            CommandText = "UPDATE t SET ref = :PaymentRef WHERE id = :InvoiceId"
        };
        var parameters = new OracleBindByNameParameters(new { PaymentRef = "PR-1", InvoiceId = "INV-9" });

        ((Dapper.SqlMapper.IDynamicParameters)parameters).AddParameters(command, NewDapperIdentity(command.CommandText, connection));

        Assert.True(command.BindByName);
        Assert.Equal(2, command.Parameters.Count);
    }

    // Dapper's Identity constructors are all internal; build one reflectively for the test.
    private static Dapper.SqlMapper.Identity NewDapperIdentity(string sql, System.Data.IDbConnection connection)
    {
        var ctor = typeof(Dapper.SqlMapper.Identity).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            binder: null,
            [typeof(string), typeof(System.Data.CommandType?), typeof(System.Data.IDbConnection), typeof(Type), typeof(Type)],
            modifiers: null);
        return (Dapper.SqlMapper.Identity)ctor!.Invoke([sql, null, connection, null, null]);
    }
}
