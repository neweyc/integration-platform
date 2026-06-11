using System.Text;
using System.Text.Json;

namespace Serto.Sdk;

/// <summary>
/// Publishes messages from inside a running integration. A published message is delivered to every
/// integration that subscribes to its subject (see <see cref="MessageIntegrationAttribute"/>).
/// Publishing is fire-and-forget from the integration's perspective: it enqueues for delivery and
/// returns; it does not block on subscribers running.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publish a typed message. The subject is resolved from the message type via
    /// <see cref="MessageSubject.For(Type)"/> (a <see cref="MessageAttribute"/> if present, otherwise
    /// a kebab-case of the type name), and the message is serialized to JSON as the body.
    /// </summary>
    Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default) where TMessage : class;

    /// <summary>
    /// Publish to an explicit subject with an arbitrary payload object. The escape hatch for callers
    /// that do not want to model a message type.
    /// </summary>
    Task PublishAsync(string subject, object payload, CancellationToken ct = default);
}

/// <summary>
/// Optionally declares the wire subject for a message type. When absent, the subject is derived from
/// the type name. The subject — not the .NET type — is the contract between publisher and subscriber,
/// so an explicit attribute is recommended for any message shared across packages.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageAttribute(string subject) : Attribute
{
    public string Subject { get; } = subject;
}

/// <summary>
/// Resolves the wire subject for a message type: the <see cref="MessageAttribute"/> value if present,
/// otherwise a kebab-case of the type's short name (e.g. <c>HighWindDetected</c> → <c>high-wind-detected</c>).
/// </summary>
public static class MessageSubject
{
    public static string For<T>() => For(typeof(T));

    public static string For(Type type)
    {
        var attribute = (MessageAttribute?)Attribute.GetCustomAttribute(type, typeof(MessageAttribute));
        return attribute is not null ? attribute.Subject : ToKebabCase(type.Name);
    }

    private static string ToKebabCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && !char.IsUpper(name[i - 1]))
                    builder.Append('-');
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// The single JSON configuration used for message bodies on both the publish and consume sides, so a
/// message round-trips through <see cref="IMessagePublisher"/> and <see cref="IIntegrationContext.PayloadAs{T}"/>.
/// </summary>
public static class MessageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
