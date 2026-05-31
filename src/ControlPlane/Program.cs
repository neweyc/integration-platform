using System.Text;
using ControlPlane.Features.Auth;
using ControlPlane.Features.Integrations;
using ControlPlane.Features.Secrets;
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

// Only redirect to HTTPS in development — in production the container runs HTTP
// behind a reverse proxy that handles TLS termination
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapSecretEndpoints();
app.MapIntegrationEndpoints();

app.Run();
