using Serto.Connectors.Sql;
using Serto.Testing;

namespace Connectors.Tests;

public class SqlConnectorTests
{
    private static TestIntegrationContext Context() => new();

    [Fact]
    public void Constructor_ValidConnectionString_Succeeds()
    {
        var exception = Record.Exception(() =>
            new SqlConnector(Context(), "Server=localhost;Database=app;User Id=sa;Password=secret;"));

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_MalformedConnectionString_Throws()
    {
        // A common mistake: a non-connection-string value (e.g. an API key) put in the SQL secret.
        var exception = Assert.Throws<ArgumentException>(() =>
            new SqlConnector(Context(), "not-a-real-connection-string"));

        Assert.Contains("malformed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_EmptyConnectionString_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SqlConnector(Context(), "   "));
    }

    [Fact]
    public void Extension_MissingSecret_ThrowsClearError()
    {
        var context = Context();

        var exception = Assert.Throws<InvalidOperationException>(() => context.SqlConnector("MISSING_CONN"));

        Assert.Contains("MISSING_CONN", exception.Message, StringComparison.Ordinal);
    }
}
