using Licensing;
using Shared.Domain;

namespace ControlPlane.Features.Licensing;

// Configuration from the "License" section. A self-hosted operator pastes their token into License:Key,
// or points License:FilePath at a file containing it. With neither set the deployment runs as Community.
public class LicenseOptions
{
    // The license token, inline. Ignored if FilePath is set and readable.
    public string? Key { get; set; }

    // Path to a file containing the license token (takes precedence over Key).
    public string? FilePath { get; set; }

    // Days a license keeps working after its expiry date before degrading to Community caps. The grace
    // window exists so a lapsed renewal never hard-stops a running production system.
    public int GraceDays { get; set; } = 14;

    // Test seam: overrides the embedded vendor public key. Null in production (uses LicensePublicKey).
    public string? PublicKeyOverride { get; set; }
}

public enum LicenseState
{
    // No license configured — the deployment runs as Community (Free caps). The normal unlicensed state.
    Unlicensed,

    // A license is configured but its signature didn't verify (tampered, corrupt, or signed by the wrong
    // key). Treated like Community for caps, but surfaced loudly because it signals a problem.
    Invalid,

    // Valid and within its term.
    Valid,

    // Past expiry but still within the grace window — caps still lifted, with warnings.
    Grace,

    // Past expiry and grace — degraded to Community caps.
    Expired
}

// What the deployment's license currently entitles, evaluated live (so expiry/grace transitions take
// effect without a restart).
public record LicenseInfo(
    LicenseState State,
    string? Licensee,
    BillingPlan Plan,
    DateTime? Expiry,
    DateTime? GraceUntil);

public interface ILicenseService
{
    // The current license status, for surfacing edition/expiry in the UI and logs.
    LicenseInfo Current { get; }

    // The plan that should govern a tenant's caps right now, given the tenant's own plan. On self-hosted a
    // valid (or in-grace) license lifts the tenant to the licensed plan; an expired or absent license
    // leaves the tenant on its own plan (Community on-prem). On cloud (Stripe configured) the license is
    // ignored entirely and Stripe-managed plans govern.
    BillingPlan EffectivePlanFor(BillingPlan tenantPlan);
}

public class LicenseService : ILicenseService
{
    private readonly bool _stripeConfigured;
    private readonly int _graceDays;
    private readonly LicensePayload? _payload;
    private readonly bool _tokenPresentButInvalid;
    private readonly TimeProvider _timeProvider;

    public LicenseService(
        LicenseOptions options,
        Billing.StripeOptions stripeOptions,
        ILogger<LicenseService> logger,
        TimeProvider timeProvider)
    {
        _stripeConfigured = stripeOptions.IsConfigured;
        _graceDays = Math.Max(0, options.GraceDays);
        _timeProvider = timeProvider;

        var token = ResolveToken(options, logger);
        if (string.IsNullOrWhiteSpace(token))
        {
            // Unlicensed Community is a normal, expected state — note it quietly.
            logger.LogInformation("No commercial license configured; running as Community edition.");
            return;
        }

        var publicKey = Base64Url.Decode(options.PublicKeyOverride ?? LicensePublicKey.Base64);
        if (LicenseToken.TryVerify(token, publicKey, out var payload) && payload is not null)
        {
            _payload = payload;
            logger.LogInformation(
                "Commercial license loaded: {Plan} for '{Licensee}', expires {Expiry:yyyy-MM-dd}.",
                payload.Plan, payload.Licensee, payload.Expiry);
        }
        else
        {
            _tokenPresentButInvalid = true;
            logger.LogError(
                "A license token is configured but failed signature verification. Running as Community " +
                "edition. Check that the token is intact and was issued for this build.");
        }
    }

    public LicenseInfo Current
    {
        get
        {
            if (_payload is null)
            {
                var state = _tokenPresentButInvalid ? LicenseState.Invalid : LicenseState.Unlicensed;
                return new LicenseInfo(state, null, BillingPlan.Free, null, null);
            }

            var graceUntil = _payload.Expiry.AddDays(_graceDays);
            return new LicenseInfo(StateOf(_payload), _payload.Licensee, _payload.Plan, _payload.Expiry, graceUntil);
        }
    }

    public BillingPlan EffectivePlanFor(BillingPlan tenantPlan)
    {
        // Cloud: Stripe governs plans per tenant; an instance license has no place here.
        if (_stripeConfigured || _payload is null)
            return tenantPlan;

        return StateOf(_payload) switch
        {
            LicenseState.Valid or LicenseState.Grace => Higher(_payload.Plan, tenantPlan),
            _ => tenantPlan // Expired beyond grace → degrade to the tenant's own plan (Community on-prem).
        };
    }

    private LicenseState StateOf(LicensePayload payload)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (now < payload.Expiry)
            return LicenseState.Valid;
        if (now < payload.Expiry.AddDays(_graceDays))
            return LicenseState.Grace;
        return LicenseState.Expired;
    }

    private static BillingPlan Higher(BillingPlan a, BillingPlan b) => (BillingPlan)Math.Max((int)a, (int)b);

    private static string? ResolveToken(LicenseOptions options, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(options.FilePath))
        {
            if (File.Exists(options.FilePath))
                return File.ReadAllText(options.FilePath).Trim();

            logger.LogError("License:FilePath '{Path}' does not exist; falling back to License:Key.", options.FilePath);
        }

        return options.Key?.Trim();
    }
}
