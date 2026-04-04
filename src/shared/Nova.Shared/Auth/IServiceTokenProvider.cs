namespace Nova.Shared.Auth;

/// <summary>
/// Issues and caches short-lived JWT tokens used by this service when calling other internal
/// Nova services. The token identifies the <em>service</em>, not an end user.
/// </summary>
/// <remarks>
/// <para><b>Why a separate token from the user JWT?</b></para>
/// The user JWT (<c>Authorization: Bearer ...</c>) identifies the end user. Service-to-service
/// calls are made by the service itself — there may be no logged-in user at all (background jobs,
/// scheduled tasks, outbox relay). The internal token carries the service name as the subject
/// and a dedicated audience (<c>nova-internal</c>) so receiving services can distinguish
/// internal calls from user-facing requests.
///
/// <para><b>Token caching</b></para>
/// Token generation is not free. <see cref="GetTokenAsync"/> caches the current token and
/// returns it on repeated calls. A new token is generated only when the cached one is within
/// 30 seconds of expiry. This means every outbound call does not incur a crypto operation.
///
/// <para><b>Usage</b></para>
/// Inject <see cref="IServiceTokenProvider"/> into a <c>DelegatingHandler</c> registered on
/// the internal <c>HttpClient</c>. Do not call it directly from endpoint handlers.
/// </remarks>
public interface IServiceTokenProvider
{
    /// <summary>
    /// Returns a valid <c>Bearer</c> token string for outbound internal calls.
    /// The token is cached and renewed automatically when close to expiry.
    /// </summary>
    Task<string> GetTokenAsync(CancellationToken ct = default);
}
