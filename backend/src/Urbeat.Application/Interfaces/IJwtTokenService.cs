using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IJwtTokenService
{
    AuthTokenPairDto GenerateToken(string email, Guid userId, IReadOnlyCollection<string> roles);
}
