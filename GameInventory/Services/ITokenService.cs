using System.Security.Claims;

namespace GameInventory.Services
{
    public interface ITokenService
    {
        string GenerateJwtToken(ApplicationUser user);
        string GenerateEmailVerificationToken(string email);
        ClaimsPrincipal ValidateToken(string token, bool validateLifetime = true);
    }
}
