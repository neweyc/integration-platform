using IntegrationPlatform.Sdk;
using IntegrationPlatform.Connectors.Sql;
using IntegrationPlatform.Connectors.Http;
using Microsoft.Extensions.Logging;

namespace Examples.SqlToHttp;

[ScheduledIntegration("SQL to API Sync", "sql-to-http", "0 */30 * * *", Description = "Sync pending orders from SQL to an external API every 30 minutes.")]
public class OrderSyncIntegration : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        var db = context.SqlConnector("DB_CONNECTION_STRING");
        var api = context.HttpConnector("https://api.erp.com").WithBearerToken("ERP_API_KEY");

        context.Logger.LogInformation("Fetching pending orders from database...");

        var orders = await db.QueryAsync<Order>("SELECT * FROM Orders WHERE Status = 'Pending'", ct: ct);

        foreach (var order in orders)
        {
            context.Logger.LogInformation("Syncing order {OrderId}...", order.Id);
            
            await api.PostJsonAsync("/orders", order, ct);
            
            await db.ExecuteAsync("UPDATE Orders SET Status = 'Synced' WHERE Id = @Id", new { order.Id }, ct);
        }

        context.Logger.LogInformation("Sync completed.");
    }

    public record Order(Guid Id, string CustomerName, decimal Total);
}
