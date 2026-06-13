using System.Text;
using System.Text.Json.Serialization;
using ControlPlane.Features.AgentTokens;
using ControlPlane.Features.Alerts;
using ControlPlane.Features.Alerts.Email;
using ControlPlane.Features.AuditLog;
using ControlPlane.Features.Auth;
using ControlPlane.Features.Billing;
using ControlPlane.Features.Environments;
using ControlPlane.Features.Health;
using ControlPlane.Features.InfoRequest;
using ControlPlane.Features.IntegrationPackages;
using ControlPlane.Features.IntegrationPackages.Scanning;
using ControlPlane.Features.Integrations;
using ControlPlane.Features.Invitations;
using ControlPlane.Features.Licensing;
using ControlPlane.Features.Messages;
using ControlPlane.Features.Onboarding;
using ControlPlane.Features.Secrets;
using ControlPlane.Features.Setup;
using ControlPlane.Features.Tenants;
using ControlPlane.Features.Triggers;
using ControlPlane.Features.UserTokens;
using ControlPlane.Features.Webhooks;
using ControlPlane.Features.Workflows;
using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using ControlPlane.Infrastructure.Maintenance;
using ControlPlane.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization to use string enum values instead of integers
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();

// Soft-launch / preview circuit breaker: Maintenance__Enabled=true makes the control plane reject all
// writes (POST/PUT/PATCH/DELETE) with 503, so nothing can be written to the database.
builder.Services.AddMaintenanceMode(builder.Configuration);

// Public "request more info" form — emails submissions (no DB write).
builder.Services.Configure<InfoRequestOptions>(builder.Configuration.GetSection(InfoRequestOptions.SectionName));
builder.Services.AddScoped<InfoRequestHandler>();

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

// Rate limiting — a generous global per-IP limit plus a strict policy for sensitive auth endpoints.
builder.Services.AddSertoRateLimiting(builder.Configuration);

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
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IAuthTokenIssuer, AuthTokenIssuer>();
builder.Services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
builder.Services.AddScoped<IPasswordResetNotifier, PasswordResetNotifier>();
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
builder.Services.AddScoped<IRoutingHealthRepository, RoutingHealthRepository>();
builder.Services.AddScoped<ICommandHandler<GetUnroutableIntegrationsCommand, UnroutableIntegrationsResult>, GetUnroutableIntegrationsHandler>();
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
builder.Services.AddScoped<IManifestReader, ManifestReader>();
builder.Services.AddScoped<PackageRepository>();
builder.Services.AddScoped<IPackageRepository>(sp => sp.GetRequiredService<PackageRepository>());
builder.Services.AddScoped<IPackageReadRepository>(sp => sp.GetRequiredService<PackageRepository>());
builder.Services.AddScoped<IPackageDeleteRepository>(sp => sp.GetRequiredService<PackageRepository>());
builder.Services.AddScoped<IPackageActivationRepository>(sp => sp.GetRequiredService<PackageRepository>());
builder.Services.AddScoped<ICommandHandler<UploadPackageCommand, PackageUploadResult>, UploadPackageHandler>();
builder.Services.AddScoped<ICommandHandler<ActivatePackageVersionCommand, ActivatePackageVersionResult>, ActivatePackageVersionHandler>();
builder.Services.AddScoped<ICommandHandler<ListPackagesCommand, ListPackagesResult>, ListPackagesHandler>();
builder.Services.AddScoped<ICommandHandler<GetPackageCommand, PackageMetadata?>, GetPackageHandler>();
builder.Services.AddScoped<ICommandHandler<DownloadPackageCommand, DownloadPackageResult?>, DownloadPackageHandler>();
builder.Services.AddScoped<ICommandHandler<DeletePackageCommand, bool>, DeletePackageHandler>();

// Secrets feature
builder.Services.AddScoped<ISecretRepository, SecretRepository>();
builder.Services.AddScoped<ISecretReadRepository, SecretRepository>();
builder.Services.AddScoped<ISecretDeleteRepository, SecretRepository>();
// Secret value storage backend, selected by Secrets:Backend (default "Embedded"). Embedded = values
// encrypted in the control-plane DB (today's behavior). ExternalVault = the control plane stores only
// references; values live in a vault on the customer's iron and the agent resolves them. Hosted
// deployments mandate ExternalVault so secret material never rests here. See docs/secret-vault.md.
var secretBackend = builder.Configuration["Secrets:Backend"];
if (string.Equals(secretBackend, "ExternalVault", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddScoped<ISecretBackend, ExternalVaultSecretBackend>();
else
    builder.Services.AddScoped<ISecretBackend, EmbeddedSecretBackend>();
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

// Message publish/subscribe — delivers a published message to subscribing integrations
builder.Services.AddScoped<ICommandHandler<PublishMessageCommand, PublishMessageResult>, PublishMessageHandler>();

// Orphaned execution reaper — backstop for executions stuck Running after an agent crash/disconnect
builder.Services.AddSingleton(
    builder.Configuration.GetSection("OrphanedExecutionReaper").Get<OrphanedExecutionReaperOptions>()
    ?? new OrphanedExecutionReaperOptions());
builder.Services.AddScoped<IOrphanedExecutionRepository, OrphanedExecutionRepository>();
builder.Services.AddScoped<ICommandHandler<ReapOrphanedExecutionsCommand, ReapOrphanedExecutionsResult>, ReapOrphanedExecutionsHandler>();
builder.Services.AddHostedService<OrphanedExecutionReaper>();

// Failed-integration alerts
builder.Services.AddSingleton(
    builder.Configuration.GetSection("Zepto").Get<ZeptoOptions>() ?? new ZeptoOptions());
var alertWebhookOptions =
    builder.Configuration.GetSection("AlertWebhooks").Get<AlertWebhookOptions>() ?? new AlertWebhookOptions();
builder.Services.AddSingleton(alertWebhookOptions);
builder.Services.AddHttpClient(); // ZeptoMail sender uses the default IHttpClientFactory client
// The webhook sender uses a dedicated, SSRF-guarded client unless the operator allows private targets.
var webhookHttpClient = builder.Services.AddHttpClient(WebhookAlertSender.HttpClientName);
if (!alertWebhookOptions.AllowPrivateNetworkTargets)
{
    webhookHttpClient.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        ConnectCallback = OutboundWebhookGuard.CreateGuardedConnectCallback()
    });
}
builder.Services.AddSingleton<AlertDispatchQueue>();
builder.Services.AddSingleton<IAlertDispatchQueue>(sp => sp.GetRequiredService<AlertDispatchQueue>());
builder.Services.AddSingleton<IEmailSender, ZeptoEmailSender>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<IWebhookAlertSender, WebhookAlertSender>();
builder.Services.AddScoped<AlertSettingsRepository>();
builder.Services.AddScoped<IAlertSettingsReadRepository>(sp => sp.GetRequiredService<AlertSettingsRepository>());
builder.Services.AddScoped<IAlertSettingsWriteRepository>(sp => sp.GetRequiredService<AlertSettingsRepository>());
builder.Services.AddScoped<IAlertNotifier, AlertNotifier>();
builder.Services.AddScoped<ICommandHandler<GetTenantAlertSettingsCommand, TenantAlertSettingsDto>, GetTenantAlertSettingsHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateTenantAlertSettingsCommand, TenantAlertSettingsDto>, UpdateTenantAlertSettingsHandler>();
builder.Services.AddScoped<ICommandHandler<GetIntegrationAlertSettingsCommand, IntegrationAlertSettingsDto>, GetIntegrationAlertSettingsHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateIntegrationAlertSettingsCommand, IntegrationAlertSettingsDto>, UpdateIntegrationAlertSettingsHandler>();
builder.Services.AddScoped<ICommandHandler<SendTestAlertCommand, AlertSendOutcome>, SendTestAlertHandler>();
builder.Services.AddHostedService<AlertDispatchService>();

// Unroutable-work monitor — periodically alerts when an integration's required agent capabilities
// are offered by no live agent, reusing the alert channels above.
builder.Services.AddSingleton(
    builder.Configuration.GetSection("UnroutableWorkMonitor").Get<UnroutableWorkMonitorOptions>()
    ?? new UnroutableWorkMonitorOptions());
builder.Services.AddScoped<IUnroutableAlertRepository, UnroutableAlertRepository>();
builder.Services.AddScoped<ICommandHandler<MonitorUnroutableWorkCommand, MonitorUnroutableWorkResult>, MonitorUnroutableWorkHandler>();
builder.Services.AddHostedService<UnroutableWorkMonitor>();

// Environments feature
builder.Services.AddScoped<EnvironmentRepository>();
builder.Services.AddScoped<IEnvironmentReadRepository>(sp => sp.GetRequiredService<EnvironmentRepository>());
builder.Services.AddScoped<IEnvironmentWriteRepository>(sp => sp.GetRequiredService<EnvironmentRepository>());
builder.Services.AddScoped<ICommandHandler<ListEnvironmentsCommand, ListEnvironmentsResult>, ListEnvironmentsHandler>();
builder.Services.AddScoped<ICommandHandler<CreateEnvironmentCommand, EnvironmentDto>, CreateEnvironmentHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateEnvironmentCommand, EnvironmentDto>, UpdateEnvironmentHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteEnvironmentCommand, bool>, DeleteEnvironmentHandler>();

// Auth feature
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserReadRepository, UserRepository>();
builder.Services.AddScoped<IUserListRepository, UserRepository>();
builder.Services.AddScoped<ICommandHandler<RegisterUserCommand, RegisterUserResult>, RegisterUserHandler>();
builder.Services.AddScoped<ICommandHandler<ListUsersCommand, ListUsersResult>, ListUsersHandler>();
builder.Services.AddScoped<ICommandHandler<LoginUserCommand, LoginUserResult>, LoginUserHandler>();
builder.Services.AddScoped<ICommandHandler<RefreshSessionCommand, AuthTokens>, RefreshSessionHandler>();
builder.Services.AddScoped<ICommandHandler<LogoutCommand, bool>, LogoutHandler>();
builder.Services.AddScoped<ICommandHandler<ForgotPasswordCommand, bool>, ForgotPasswordHandler>();
builder.Services.AddScoped<ICommandHandler<ResetPasswordCommand, bool>, ResetPasswordHandler>();

// Onboarding feature
builder.Services.AddScoped<IOnboardingRepository, OnboardingRepository>();
builder.Services.AddScoped<ICommandHandler<GetOnboardingStatusCommand, OnboardingStatusResult>, GetOnboardingStatusHandler>();

// Billing feature (Stripe). Inert unless a Stripe secret key is configured.
builder.Services.AddSingleton(
    builder.Configuration.GetSection("Stripe").Get<StripeOptions>() ?? new StripeOptions());
builder.Services.AddSingleton<BillingPlanCatalog>();
builder.Services.AddScoped<IStripeGateway, StripeGateway>();
builder.Services.AddScoped<IBillingRepository, BillingRepository>();
builder.Services.AddScoped<ICommandHandler<GetBillingStatusCommand, BillingStatusResult>, GetBillingStatusHandler>();
builder.Services.AddScoped<ICommandHandler<CreateCheckoutSessionCommand, BillingUrlResult>, CreateCheckoutSessionHandler>();
builder.Services.AddScoped<ICommandHandler<CreatePortalSessionCommand, BillingUrlResult>, CreatePortalSessionHandler>();
builder.Services.AddScoped<ICommandHandler<HandleStripeWebhookCommand, bool>, HandleStripeWebhookHandler>();

// Commercial licensing (self-hosted). A signed Ed25519 license token lifts the deployment to its paid
// plan — the on-prem analog of the Stripe webhook. Loaded and verified once at startup against the
// embedded public key; the active plan is evaluated live so expiry/grace degrade without a restart.
// Inert (Community) when no token is configured. See docs/licensing.md.
builder.Services.AddSingleton(
    builder.Configuration.GetSection("License").Get<LicenseOptions>() ?? new LicenseOptions());
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILicenseService, LicenseService>();

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

// Maintenance circuit breaker sits after static files (so the SPA + docs still render) and before
// everything else, so when enabled it short-circuits every write before it can reach the database.
app.UseMaintenanceMode();

// Rate limiting sits after static files (so SPA assets aren't throttled) and before auth. It is
// skipped entirely when disabled — tests turn it off so repeated requests don't trip the limiter.
var rateLimitOptions = app.Services.GetRequiredService<IOptions<RateLimitOptions>>().Value;
if (rateLimitOptions.Enabled)
{
    app.UseRateLimiter();
}

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.UseUserTokenAuthentication();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapMaintenanceEndpoint();
app.MapInfoRequestEndpoints();
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
app.MapAlertEndpoints();
app.MapEnvironmentEndpoints();
app.MapOnboardingEndpoints();
app.MapBillingEndpoints();
app.MapLicenseEndpoints();

// Fallback: any request that didn't match an API route returns index.html
// so that React Router can handle client-side navigation.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
