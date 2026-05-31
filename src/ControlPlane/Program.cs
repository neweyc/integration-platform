using System.Text;
using ControlPlane.Features.AgentTokens;
using ControlPlane.Features.Auth;
using ControlPlane.Features.Integrations;
using ControlPlane.Features.Secrets;
using ControlPlane.Features.Setup;
using ControlPlane.Features.Tenants;
using ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Auth
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret must be configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// In development, allow requests from the Vite dev server on port 5173
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.WithOrigins("http://localhost:5173")
                  .AllowAnyHeader()
                  .AllowAnyMethod());
    });
}

// Dispatcher
builder.Services.AddScoped<IDispatcher, Dispatcher>();

// Infrastructure services
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IEncryptionService, AesEncryptionService>();

// Tenant feature
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<ITenantReadRepository, TenantRepository>();
builder.Services.AddScoped<ICommandHandler<CreateTenantCommand, CreateTenantResult>, CreateTenantHandler>();
builder.Services.AddScoped<ICommandHandler<GetTenantCommand, GetTenantResult?>, GetTenantHandler>();

// Setup feature
builder.Services.AddScoped<ISetupRepository, SetupRepository>();
builder.Services.AddScoped<ICommandHandler<SetupCommand, SetupResult>, SetupHandler>();

// Integrations feature
builder.Services.AddScoped<IIntegrationRepository, IntegrationRepository>();
builder.Services.AddScoped<IIntegrationReadRepository, IntegrationRepository>();
builder.Services.AddScoped<IIntegrationUpdateRepository, IntegrationRepository>();
builder.Services.AddScoped<IIntegrationDeleteRepository, IntegrationRepository>();
builder.Services.AddScoped<ICommandHandler<CreateIntegrationCommand, CreateIntegrationResult>, CreateIntegrationHandler>();
builder.Services.AddScoped<ICommandHandler<GetIntegrationCommand, CreateIntegrationResult?>, GetIntegrationHandler>();
builder.Services.AddScoped<ICommandHandler<ListIntegrationsCommand, ListIntegrationsResult>, ListIntegrationsHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateIntegrationCommand, CreateIntegrationResult>, UpdateIntegrationHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteIntegrationCommand, bool>, DeleteIntegrationHandler>();

// Secrets feature
builder.Services.AddScoped<ISecretRepository, SecretRepository>();
builder.Services.AddScoped<ISecretReadRepository, SecretRepository>();
builder.Services.AddScoped<ISecretDeleteRepository, SecretRepository>();
builder.Services.AddScoped<ICommandHandler<SetSecretCommand, SetSecretResult>, SetSecretHandler>();
builder.Services.AddScoped<ICommandHandler<ListSecretsCommand, ListSecretsResult>, ListSecretsHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteSecretCommand, bool>, DeleteSecretHandler>();
builder.Services.AddScoped<ICommandHandler<GetSecretBundleCommand, GetSecretBundleResult>, GetSecretBundleHandler>();

// Agent tokens feature
builder.Services.AddScoped<IAgentTokenService, AgentTokenService>();
builder.Services.AddScoped<AgentTokenRepository>();
builder.Services.AddScoped<IAgentTokenRepository>(sp => sp.GetRequiredService<AgentTokenRepository>());
builder.Services.AddScoped<IAgentTokenReadRepository>(sp => sp.GetRequiredService<AgentTokenRepository>());
builder.Services.AddScoped<IAgentTokenDeleteRepository>(sp => sp.GetRequiredService<AgentTokenRepository>());
builder.Services.AddScoped<IAgentTokenLookupRepository>(sp => sp.GetRequiredService<AgentTokenRepository>());
builder.Services.AddScoped<ICommandHandler<CreateAgentTokenCommand, CreateAgentTokenResult>, CreateAgentTokenHandler>();
builder.Services.AddScoped<ICommandHandler<ListAgentTokensCommand, ListAgentTokensResult>, ListAgentTokensHandler>();
builder.Services.AddScoped<ICommandHandler<RevokeAgentTokenCommand, bool>, RevokeAgentTokenHandler>();

// Auth feature
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserReadRepository, UserRepository>();
builder.Services.AddScoped<ICommandHandler<RegisterUserCommand, RegisterUserResult>, RegisterUserHandler>();
builder.Services.AddScoped<ICommandHandler<LoginUserCommand, LoginUserResult>, LoginUserHandler>();

// Exception handling
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

// Apply any pending database migrations on startup.
// This means running `docker-compose up` is all you need — no manual migration step.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseExceptionHandler();


// Serve the React app's static files from wwwroot.
// UseDefaultFiles must come before UseStaticFiles so that a request
// to "/" is rewritten to "/index.html" before the static file middleware handles it.
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapSetupEndpoints();
app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapSecretEndpoints();
app.MapIntegrationEndpoints();
app.MapAgentTokenEndpoints();

// Fallback: any request that didn't match an API route returns index.html
// so that React Router can handle client-side navigation.
app.MapFallbackToFile("index.html");

app.Run();
