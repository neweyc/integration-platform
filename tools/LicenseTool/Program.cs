using System.Globalization;
using Licensing;
using Shared.Domain;

// serto-license — vendor tool for issuing commercial license keys.
//
//   serto-license keygen [--out <dir>]
//       Generate an Ed25519 keypair. Writes <dir>/license-signing.key (private) and
//       license-signing.pub (public), and prints the public key to embed in the control plane.
//
//   serto-license issue --key <privateKeyFile> --licensee "Acme Corp" --plan Business
//                       --expires 2027-01-01 [--issued 2026-06-10] [--max-tenants 1]
//       Sign and print a license token for the customer.

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0)
    {
        PrintUsage();
        return 1;
    }

    try
    {
        return args[0] switch
        {
            "keygen" => Keygen(ParseOptions(args[1..])),
            "issue" => Issue(ParseOptions(args[1..])),
            "-h" or "--help" or "help" => PrintUsage(),
            _ => Fail($"Unknown command '{args[0]}'.")
        };
    }
    catch (ArgumentException ex)
    {
        return Fail(ex.Message);
    }
}

static int Keygen(Dictionary<string, string> options)
{
    var outDir = options.GetValueOrDefault("out", ".");
    Directory.CreateDirectory(outDir);

    var (publicKey, privateKey) = Ed25519Keys.Generate();
    var privatePath = Path.Combine(outDir, "license-signing.key");
    var publicPath = Path.Combine(outDir, "license-signing.pub");

    File.WriteAllText(privatePath, Base64Url.Encode(privateKey));
    File.WriteAllText(publicPath, Base64Url.Encode(publicKey));

    Console.WriteLine($"Private signing key written to {privatePath}  (KEEP SECRET — never commit)");
    Console.WriteLine($"Public key written to        {publicPath}");
    Console.WriteLine();
    Console.WriteLine("Embed this public key in the control plane (LicensePublicKey.Base64):");
    Console.WriteLine(Base64Url.Encode(publicKey));
    return 0;
}

static int Issue(Dictionary<string, string> options)
{
    var keyFile = Required(options, "key");
    var licensee = Required(options, "licensee");
    var planText = Required(options, "plan");
    var expiresText = Required(options, "expires");

    if (!Enum.TryParse<BillingPlan>(planText, ignoreCase: true, out var plan))
        throw new ArgumentException($"Unknown plan '{planText}'. Valid: {string.Join(", ", Enum.GetNames<BillingPlan>())}.");

    var expiry = ParseDate(expiresText, "--expires");
    var issuedAt = options.TryGetValue("issued", out var issuedText)
        ? ParseDate(issuedText, "--issued")
        : DateTime.UtcNow;

    int? maxTenants = options.TryGetValue("max-tenants", out var maxText)
        ? int.Parse(maxText, CultureInfo.InvariantCulture)
        : null;

    var privateKey = Base64Url.Decode(File.ReadAllText(keyFile).Trim());
    var payload = new LicensePayload(licensee, plan, issuedAt, expiry, maxTenants);
    var token = LicenseToken.Sign(payload, privateKey);

    Console.Error.WriteLine($"Issued {plan} license to '{licensee}', expires {expiry:yyyy-MM-dd}:");
    Console.WriteLine(token);
    return 0;
}

static DateTime ParseDate(string value, string flag)
{
    if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
        throw new ArgumentException($"{flag} must be a date like 2027-01-01.");
    return DateTime.SpecifyKind(date, DateTimeKind.Utc);
}

static string Required(Dictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"--{name} is required.");

// Parses "--flag value" pairs into a dictionary.
static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Expected a --flag but got '{args[i]}'.");

        var name = args[i][2..];
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"--{name} requires a value.");

        options[name] = args[++i];
    }
    return options;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    PrintUsage();
    return 1;
}

static int PrintUsage()
{
    Console.Error.WriteLine("""
        serto-license — issue commercial license keys

        Commands:
          keygen [--out <dir>]
              Generate an Ed25519 signing keypair.

          issue --key <privateKeyFile> --licensee <name> --plan <Team|Business|Enterprise>
                --expires <yyyy-MM-dd> [--issued <yyyy-MM-dd>] [--max-tenants <n>]
              Sign and print a license token.
        """);
    return 0;
}
