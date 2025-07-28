namespace GameInventory.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

/// <summary>
/// Service responsible for JWT token generation and validation
/// Handles both authentication tokens and email verification tokens
/// </summary>
public class TokenService : ITokenService
{
    private readonly IConfiguration _config;

    /// <summary>
    /// Constructor that injects configuration for accessing JWT settings
    /// </summary>
    /// <param name="config">Application configuration containing JWT settings</param>
    public TokenService(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Generates a JWT authentication token for a logged-in user
    /// Used after successful login to provide stateless authentication
    /// </summary>
    /// <param name="user">The authenticated user to create a token for</param>
    /// <returns>JWT token string that can be sent to the client</returns>
    public string GenerateJwtToken(ApplicationUser user)
    {
        // Load JWT configuration settings from appsettings.json
        var settings = _config.GetSection("JwtSettings");

        // Create cryptographic key from secret string for signing the token
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings["Secret"]));

        // Define signing credentials using HMAC-SHA256 algorithm
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Create the JWT token with all necessary components
        var token = new JwtSecurityToken(
            issuer: settings["Issuer"],     // Who created this token (your app)
            audience: settings["Audience"], // Who this token is intended for (your app users)
            claims: new[]
            {
               // Standard JWT claim for user identifier (subject)
               new Claim(JwtRegisteredClaimNames.Sub, user.Id),
               // User's email address for identification
               new Claim(JwtRegisteredClaimNames.Email, user.Email),
               // Unique token ID to prevent replay attacks and enable token revocation
               new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            },
            // Token expiration time (configurable minutes from now)
            expires: DateTime.UtcNow.AddMinutes(int.Parse(settings["ExpiryMinutes"])),
            // Cryptographic signature to ensure token integrity
            signingCredentials: creds
        );

        // Serialize the token object into a JWT string for transmission
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Generates a short-lived JWT token specifically for email verification
    /// Contains only email claim and has shorter expiration for security
    /// </summary>
    /// <param name="email">Email address to verify</param>
    /// <returns>JWT token string for email verification link</returns>
    public string GenerateEmailVerificationToken(string email)
    {
        // Reuse JWT settings but create a more limited token
        var settings = _config.GetSection("JwtSettings");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings["Secret"]));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Alternative way to create JWT using SecurityTokenDescriptor
        var descriptor = new SecurityTokenDescriptor
        {
            // Create identity with only email claim (minimal information for security)
            Subject = new ClaimsIdentity(new[]
            {
               new Claim("email", email)
           }),
            // Short expiration (15 minutes default) for email verification security
            Expires = DateTime.UtcNow.AddMinutes(int.Parse(settings["EmailTokenExpiryMinutes"] ?? "15")),
            SigningCredentials = creds
        };

        var handler = new JwtSecurityTokenHandler();
        // Create token from descriptor and serialize to string
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    /// <summary>
    /// Validates a JWT token and extracts the user's claims
    /// Used by authentication middleware and manual token verification
    /// </summary>
    /// <param name="token">JWT token string to validate</param>
    /// <param name="validateLifetime">Whether to check if token has expired</param>
    /// <returns>ClaimsPrincipal containing user identity and claims if valid</returns>
    /// <exception cref="SecurityTokenException">Thrown if token is invalid</exception>
    public ClaimsPrincipal ValidateToken(string token, bool validateLifetime = true)
    {
        var handler = new JwtSecurityTokenHandler();

        // Get the same secret key used for signing
        var key = Encoding.UTF8.GetBytes(_config["JwtSettings:Secret"]);

        // Validate token and return ClaimsPrincipal if successful
        return handler.ValidateToken(token, new TokenValidationParameters
        {
            // Verify the token signature using our secret key
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),

            // Skip issuer validation (could be enabled for additional security)
            ValidateIssuer = false,

            // Skip audience validation (could be enabled for additional security)  
            ValidateAudience = false,

            // Check if token has expired (can be disabled for testing)
            ValidateLifetime = validateLifetime,

            // Allow 5 minutes clock skew between servers to handle time differences
            ClockSkew = TimeSpan.FromMinutes(5)
        }, out _); // out parameter is the validated SecurityToken (ignored with _)
    }
}