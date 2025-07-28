using GameInventory.DTOs.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using GameInventory.Services;
using Microsoft.Extensions.Caching.Memory;

namespace GameInventory.Controllers
{
    /// <summary>
    /// Authentication controller handling user registration, login, password reset, and email confirmation
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        /// <summary>
        /// ASP.NET Identity UserManager for user operations
        /// </summary>
        private readonly UserManager<ApplicationUser> _userManager;

        /// <summary>
        /// ASP.NET Identity SignInManager for authentication operations
        /// </summary>
        private readonly SignInManager<ApplicationUser> _signInManager;

        /// <summary>
        /// Email service for sending confirmation and reset emails
        /// </summary>
        private readonly IEmailService _emailService;

        /// <summary>
        /// In-memory cache for storing temporary registration data
        /// </summary>
        private readonly IMemoryCache _cache;

        /// <summary>
        /// Application configuration for accessing settings
        /// </summary>
        private readonly IConfiguration _config;

        /// <summary>
        /// Initializes a new instance of the AuthController
        /// </summary>
        /// <param name="userManager">User management service</param>
        /// <param name="signInManager">Sign-in management service</param>
        /// <param name="emailService">Email sending service</param>
        /// <param name="cache">Memory cache for temporary data storage</param>
        /// <param name="config">Application configuration</param>
        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailService emailService,
            IMemoryCache cache,
            IConfiguration config)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailService = emailService;
            _cache = cache;
            _config = config;
        }

        /// <summary>
        /// Initiates user registration by sending a confirmation email
        /// Two-step process: 1) Store registration data temporarily, 2) Send confirmation email
        /// </summary>
        /// <param name="request">Registration details (email, password, etc.)</param>
        /// <returns>Success message indicating confirmation email was sent</returns>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            // Validate the incoming request model
            if (!ModelState.IsValid)
                return BadRequest(new
                {
                    errors = ModelState.Values.SelectMany(v => v.Errors)
                                               .Select(e => e.ErrorMessage)
                });

            // Check if user already exists to prevent duplicate registrations
            if (await _userManager.FindByEmailAsync(request.Email) != null)
                return BadRequest(new { message = "User already exists." });

            // Generate a unique token for email confirmation
            var token = Guid.NewGuid().ToString();

            // Store registration data in cache with 15-minute expiration
            // This allows us to complete registration after email confirmation
            _cache.Set(token, request, TimeSpan.FromMinutes(15));

            // Generate confirmation link that points to our confirmation endpoint
            var link = Url.Action("ConfirmEmail", "Auth", new { token }, Request.Scheme);

            // Send confirmation email to the user
            await _emailService.SendEmailAsync(
                request.Email,
                "Confirm your GameInventory account",
                $"Please click this link to confirm your account:\n\n{link}"
            );

            return Ok(new { message = "Confirmation email sent. Please verify to complete registration." });
        }

        /// <summary>
        /// Completes user registration after email confirmation
        /// Creates the actual user account using cached registration data
        /// </summary>
        /// <param name="token">Confirmation token from email link</param>
        /// <returns>Success message indicating account creation</returns>
        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail(string token)
        {
            // Retrieve registration data from cache using the token
            if (!_cache.TryGetValue<RegisterRequest>(token, out var request))
                return BadRequest(new { message = "Invalid or expired confirmation link." });

            // Double-check that user doesn't already exist (race condition protection)
            if (await _userManager.FindByEmailAsync(request.Email) != null)
                return BadRequest(new { message = "User already exists." });

            // Create the ApplicationUser entity
            var user = new ApplicationUser { UserName = request.Email, Email = request.Email };

            // Attempt to create the user with the provided password
            var result = await _userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
                return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

            // Generate and immediately confirm the email confirmation token
            // This marks the email as confirmed since they clicked the email link
            var confirmToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            await _userManager.ConfirmEmailAsync(user, confirmToken);

            // Clean up: remove the registration data from cache
            _cache.Remove(token);

            return Ok(new { message = "Email confirmed and account created!" });
        }

        /// <summary>
        /// Authenticates a user and returns a JWT token for API access
        /// </summary>
        /// <param name="request">Login credentials (email and password)</param>
        /// <returns>JWT token on successful authentication</returns>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            // Validate the login request
            if (!ModelState.IsValid)
                return BadRequest(new
                {
                    errors = ModelState.Values.SelectMany(v => v.Errors)
                                               .Select(e => e.ErrorMessage)
                });

            // Find user by email address
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return Unauthorized(new
                {
                    message = "Account does not exist. If you created one, please confirm your email."
                });

            // Ensure the user has confirmed their email before allowing login
            if (!await _userManager.IsEmailConfirmedAsync(user))
                return Unauthorized(new
                {
                    message = "Email not confirmed. Please check your inbox."
                });

            // Verify the password without signing the user in (we'll use JWT instead of cookies)
            var signIn = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
            if (!signIn.Succeeded)
                return Unauthorized(new { message = "Incorrect password." });

            // Generate JWT token for authenticated API access
            var jwt = new TokenService(_config).GenerateJwtToken(user);
            return Ok(new { token = jwt });
        }

        /// <summary>
        /// Initiates password reset process by sending reset email
        /// Always returns success message for security (doesn't reveal if email exists)
        /// </summary>
        /// <param name="model">Contains email address for password reset</param>
        /// <returns>Generic success message</returns>
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Look up user by email
            var user = await _userManager.FindByEmailAsync(model.Email);

            // Only send email if user actually exists (but don't reveal this to caller)
            if (user != null)
            {
                // Generate secure password reset token
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                // URL encode the token to handle special characters safely
                var encoded = WebUtility.UrlEncode(token);

                // Create password reset URL that points to our GET endpoint
                var resetUrl = Url.Action("ResetPassword", "Auth", new
                {
                    token = encoded,
                    email = model.Email
                }, Request.Scheme);

                // Send password reset email
                await _emailService.SendEmailAsync(
                    model.Email,
                    "Reset your GameInventory password",
                    $"Click here to reset your password:\n\n{resetUrl}"
                );
            }

            // Always return the same message for security (prevents email enumeration)
            return Ok(new { message = "If that email exists, a reset link has been sent." });
        }

        /// <summary>
        /// GET endpoint that redirects password reset links to the frontend application
        /// This allows email links to work properly with SPA architecture
        /// </summary>
        /// <param name="token">Password reset token</param>
        /// <param name="email">User's email address</param>
        /// <returns>Redirect to frontend reset password page</returns>
        [HttpGet("reset-password")]
        public IActionResult ResetPasswordRedirect(string token, string email)
        {
            // TODO: Move frontend URL to configuration
            var frontendBase = "http://localhost:5173";

            // Construct frontend URL with encoded parameters
            var url = $"{frontendBase}/reset-password?token={WebUtility.UrlEncode(token)}&email={WebUtility.UrlEncode(email)}";

            return Redirect(url);
        }

        /// <summary>
        /// Completes password reset process with new password
        /// </summary>
        /// <param name="model">Contains email, reset token, and new password</param>
        /// <returns>Success or error message</returns>
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Find the user by email
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return BadRequest(new { message = "Invalid request." });

            // Decode the token that was URL encoded in the email
            var decodedToken = WebUtility.UrlDecode(model.Token);

            // Attempt to reset the password using the token
            var result = await _userManager.ResetPasswordAsync(user, decodedToken, model.NewPassword);

            if (!result.Succeeded)
                return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

            return Ok(new { message = "Password has been reset successfully." });
        }
    }
}