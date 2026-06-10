using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ControlPlane.Infrastructure;

// Lets `dotnet ef` (migrations add / script) construct the context at design time without booting
// the web app. Booting the app would run the startup migration step and require Jwt/DB/encryption
// configuration plus a live database — none of which migration generation needs. The connection
// string here is never opened during `migrations add`; it only has to be a valid Npgsql string.
public class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=integration_platform_design;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
