using System.Text;
using System.Text.Json.Serialization;
using ControlPlane.Features.AgentTokens;
using ControlPlane.Features.AuditLog;
using ControlPlane.Features.Auth;
using ControlPlane.Features.IntegrationPackages;
using ControlPlane.Features.IntegrationPackages.Scanning;
using ControlPlane.Features.Integrations;
using ControlPlane.Features.Invitations;
using ControlPlane.Features.Secrets;
using ControlPlane.Features.Setup;
using ControlPlane.Features.Tenants;
using ControlPlane.Features.Triggers;
using ControlPlane.Features.UserTokens;
using ControlPlane.Features.Webhooks;
using ControlPlane.Features.Workflows;
using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization to use string enum values instead of integers
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

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

// Dispatcher — wrapped so audited commands write an audit entry on success
builder.Services.AddScoped<Dispatcher>();
builder.Services.AddScoped<IAuditRecorder, AuditRecorder>();
builder.Services.AddScoped<IDispatcher, AuditingDispatcher>();

// Infrastructure services
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IEncryptionService, AesEncryptionService>();
builder.Services.AddScoped<IQuotaService, QuotaService>();

// Tenant feature
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<ITenantReadRepository, TenantRepository>();
builder.Services.AddScoped<ICommandHandler<CreateTenantCommand, CreateTenantResult>, CreateTenantHandler>();
builder.Services.AddScoped<ICommandHandler<GetTenantCommand, GetTenantResult?>, GetTenantHandler>();
builder.Services.AddScoped<ICommandHandler<RegisterTenantCommand, RegisterTenantResult>, RegisterTenantHandler>();

// Invitation feature
builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();
builder.Services.AddScoped<IInvitationReadRepository, InvitationRepository>();
builder.Services.AddScoped<IInvitationRevocationRepository, InvitationRepository>();
builder.Services.AddScoped<IInvitationResendRepository, InvitationRepository>();
builder.Services.AddScoped<ICommandHandler<InviteUserCommand, InviteUserResult>, InviteUserHandler>();
builder.Services.AddScoped<ICommandHandler<ListInvitationsCommand, ListInvitationsResult>, ListInvitationsHandler>();
builder.Services.AddScoped<ICommandHandler<RevokeInvitationCommand, bool>, RevokeInvitationHandler>();
builder.Services.AddScoped<ICommandHandler<ResendInvitationCommand, ResendInvitationResult?>, ResendInvitationHandler>();
builder.Services.AddScoped<ICommandHandler<AcceptInvitationCommand, AcceptInvitationResult>, AcceptInvitationHandler>();

// Setup feature
builder.Services.AddScoped<ISetupRepository, SetupRepository>();
builder.Services.AddScoped<ICommandHandler<SetupCommand, SetupResult>, SetupHandler>();

// Integrations feature
builder.Services.AddScoped<IIntegrationRepository, IntegrationRepository>();
builder.Services.AddScoped<IIntegrationReadRepository, IntegrationRepository>();
builder.Services.AddScoped<IIntegrationUpdateRepository, IntegrationRepository>();
builder.Services.AddScoped<IIntegrationDeleteRepository, IntegrationRepository>();
builder.Services.AddScoped<IIntegrationValidationRepository, IntegrationRepository>();
builder.Services.AddScoped<IExecutionHistoryRepository, ExecutionHistoryRepository>();
builder.Services.AddScoped<IExecutionLogReadRepository, ExecutionLogReadRepository>();
builder.Services.AddScoped<ICommandHandler<CreateIntegrationCommand, CreateIntegrationResult>, CreateIntegrationHandler>();
builder.Services.AddScoped<ICommandHandler<GetIntegrationCommand, CreateIntegrationResult?>, GetIntegrationHandler>();
builder.Services.AddScoped<ICommandHandler<ListIntegrationsCommand, ListIntegrationsResult>, ListIntegrationsHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateIntegrationCommand, CreateIntegrationResult>, UpdateIntegrationHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteIntegrationCommand, bool>, DeleteIntegrationHandler>();
builder.Services.AddScoped<ICommandHandler<ListIntegrationExecutionsCommand, ListIntegrationExecutionsResult>, ListIntegrationExecutionsHandler>();
builder.Services.AddScoped<IAuditLogReadRepository, AuditLogReadRepository>();
builder.Services.AddScoped<ICommandHandler<ListAuditLogCommand, ListAuditLogResult>, ListAuditLogHandler>();
builder.Services.AddScoped<ICommandHandler<ListExecutionLogsCommand, ListExecutionLogsResult>, ListExecutionLogsHandler>();
builder.Services.AddScoped<IManualRunRepository, ManualRunRepository>();
builder.Services.AddScoped<ICommandHandler<RequestManualRunCommand, ManualRunResult>, RequestManualRunHandler>();
builder.Services.AddScoped<ITriggerWorkItemProducer, TriggerWorkItemProducer>();
builder.Services.AddScoped<ITriggerEventRecorder, TriggerEventRecorder>();
builder.Services.AddScoped<ITriggerAdapter, ScheduledTriggerAdapter>();
builder.Services.AddScoped<ITriggerAdapter, ManualTriggerAdapter>();
builder.Services.AddScoped<ITriggerAdapter, WebhookTriggerAdapter>();
builder.Services.AddScoped<ITriggerAdapter, QueueTriggerAdapter>();
builder.Services.AddScoped<ITriggerAdapter, FileTriggerAdapter>();
builder.Services.AddScoped<ITriggerAdapterCatalog, TriggerAdapterCatalog>();

// Workflows feature
builder.Services.AddScoped<IWorkflowRepository, WorkflowRepository>();
builder.Services.AddScoped<IWorkflowProgressionService, WorkflowProgressionService>();
builder.Services.AddScoped<ICommandHandler<CreateWorkflowCommand, WorkflowDefinitionResult>, CreateWorkflowHandler>();
builder.Services.AddScoped<ICommandHandler<ListWorkflowsCommand, ListWorkflowsResult>, ListWorkflowsHandler>();
builder.Services.AddScoped<ICommandHandler<RunWorkflowCommand, WorkflowRunResult>, RunWorkflowHandler>();
builder.Services.AddScoped<ICommandHandler<ListWorkflowRunsCommand, ListWorkflowRunsResult>, ListWorkflowRunsHandler>();

// Webhooks feature
builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
builder.Services.AddScoped<ICommandHandler<DeliverWebhookCommand, DeliverWebhookResult>, DeliverWebhookHandler>();

// Integration packages feature
builder.Services.AddScoped<IAssemblyScanner, AssemblyScanner>();
builder.Services.AddScoped<PackageRepository>();
builder.Services.AddScoped<IPackageRepository>(sp => sp.GetRequiredService<PackageRepository>());
builder.Services.AddScoped<IPackageReadRepository>(sp => sp.GetRequiredService<PackageRepository>());
builder.Services.AddScoped<IPackageDeleteRepository>(sp => sp.GetRequiredService<PackageRepository>());
builder.Services.AddScoped<ICommandHandler<UploadPackageCommand, PackageUploadResult>, UploadPackageHandler>();
builder.Services.AddScoped<ICommandHandler<ListPackagesCommand, ListPackagesResult>, ListPackagesHandler>();
builder.Services.AddScoped<ICommandHandler<GetPackageCommand, PackageMetadata?>, GetPackageHandler>();
builder.Services.AddScoped<ICommandHandler<DownloadPackageCommand, DownloadPackageResult?>, DownloadPackageHandler>();
builder.Services.AddScoped<ICommandHandler<DeletePackageCommand, bool>, DeletePackageHandler>();

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
builder.Services.AddScoped<IAgentHeartbeatRepository, AgentHeartbeatRepository>();
builder.Services.AddScoped<ICommandHandler<AgentHeartbeatCommand, bool>, AgentHeartbeatHandler>();
builder.Services.AddScoped<ICommandHandler<ListAgentHeartbeatsCommand, ListAgentHeartbeatsResult>, ListAgentHeartbeatsHandler>();
builder.Services.AddScoped<PollRepository>();
builder.Services.AddScoped<IPollRepository>(sp => sp.GetRequiredService<PollRepository>());
builder.Services.AddScoped<ICommandHandler<PollIntegrationsCommand, PollIntegrationsResult>, PollIntegrationsHandler>();
builder.Services.AddScoped<IScheduleStateRepository, ScheduleStateRepository>();
builder.Services.AddScoped<WorkItemRepository>();
builder.Services.AddScoped<IWorkItemRepository>(sp => sp.GetRequiredService<WorkItemRepository>());
builder.Services.AddScoped<IPackageLookupRepository, PackageLookupRepository>();
builder.Services.AddScoped<ExecutionRepository>();
builder.Services.AddScoped<IExecutionRepository>(sp => sp.GetRequiredService<ExecutionRepository>());
builder.Services.AddScoped<IExecutionLogRepository, ExecutionLogRepository>();
builder.Services.AddScoped<IManualRunRequestRepository, ManualRunRequestRepository>();
builder.Services.AddScoped<ICommandHandler<StartExecutionCommand, StartExecutionResult>, StartExecutionHandler>();
builder.Services.AddScoped<ICommandHandler<CompleteExecutionCommand, bool>, CompleteExecutionHandler>();
builder.Services.AddScoped<ICommandHandler<RecordExecutionLogCommand, bool>, RecordExecutionLogHandler>();

// Auth feature
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserReadRepository, UserRepository>();
builder.Services.AddScoped<IUserListRepository, UserRepository>();
builder.Services.AddScoped<ICommandHandler<RegisterUserCommand, RegisterUserResult>, RegisterUserHandler>();
builder.Services.AddScoped<ICommandHandler<ListUsersCommand, ListUsersResult>, ListUsersHandler>();
builder.Services.AddScoped<ICommandHandler<LoginUserCommand, LoginUserResult>, LoginUserHandler>();

// User token feature
builder.Services.AddScoped<IUserTokenService, UserTokenService>();
builder.Services.AddScoped<IUserTokenRepository, UserTokenRepository>();
builder.Services.AddScoped<ICommandHandler<CreateUserTokenCommand, CreateUserTokenResult>, CreateUserTokenHandler>();
builder.Services.AddScoped<ICommandHandler<ListUserTokensCommand, ListUserTokensResult>, ListUserTokensHandler>();
builder.Services.AddScoped<ICommandHandler<RevokeUserTokenCommand, bool>, RevokeUserTokenHandler>();

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

app.UseUserTokenAuthentication();
app.UseAuthentication();
app.UseAuthorization();

app.MapSetupEndpoints();
app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapUserTokenEndpoints();
app.MapInvitationEndpoints();
app.MapSecretEndpoints();
app.MapIntegrationEndpoints();
app.MapTriggerAdapterEndpoints();
app.MapTriggerEventEndpoints();
app.MapPackageEndpoints();
app.MapAgentTokenEndpoints();
app.MapWebhookEndpoints();
app.MapAuditLogEndpoints();
app.MapWorkflowEndpoints();

// Fallback: any request that didn't match an API route returns index.html
// so that React Router can handle client-side navigation.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
