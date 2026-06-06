using Serto.Sdk;
using Serto.Connectors.Http;
using Microsoft.Extensions.Logging;

namespace Examples.SalesforceSync;

[ScheduledIntegration("Salesforce Opportunity Log", "salesforce-sync", "0 0 * * *", Description = "Log daily summary of Salesforce opportunities.")]
public class SalesforceOpportunityIntegration : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        var sf = context.HttpConnector("https://your-domain.my.salesforce.com/services/data/v60.0/")
                        .WithBearerToken("SALESFORCE_ACCESS_TOKEN");

        context.Logger.LogInformation("Fetching daily opportunities from Salesforce...");

        var query = "SELECT Id, Name, Amount, CloseDate FROM Opportunity WHERE CloseDate = TODAY";
        var results = await sf.GetJsonAsync<QueryResult>($"query?q={Uri.EscapeDataString(query)}", ct);

        if (results?.Records != null)
        {
            foreach (var opp in results.Records)
            {
                context.Logger.LogInformation("Opportunity: {Name} - {Amount}", opp.Name, opp.Amount);
            }
            
            context.Logger.LogInformation("Total Opportunities Today: {Count}", results.TotalSize);
        }
    }

    public record QueryResult(int TotalSize, bool Done, List<Opportunity> Records);
    public record Opportunity(string Id, string Name, decimal? Amount, string CloseDate);
}
