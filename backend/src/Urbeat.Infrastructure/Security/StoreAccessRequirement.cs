using Microsoft.AspNetCore.Authorization;

namespace Urbeat.Infrastructure.Security;

/// <summary>
/// Marker requirement for the <c>StoreAccess</c> policy. The decision is made by the server-side
/// handler, which reads the delegated context feature attached by the middleware; no client input
/// participates.
/// </summary>
public sealed class StoreAccessRequirement : IAuthorizationRequirement;
