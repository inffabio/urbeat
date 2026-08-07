using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IJwtTokenService
{
    AuthTokenResponseDto GenerateToken(string email, Guid userId, IReadOnlyCollection<string> roles);
}
