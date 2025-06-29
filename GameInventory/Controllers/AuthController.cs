using GameInventory.DTOs.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using GameInventory.Services;
using Microsoft.Extensions.Caching.Memory;
namespace GameInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailService _emailService;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;

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

        // ── REGISTER ────────────────────────────────────────────────────────────────
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new
                {
                    errors = ModelState.Values.SelectMany(v => v.Errors)
                                               .Select(e => e.ErrorMessage)
                });

            if (await _userManager.FindByEmailAsync(request.Email) != null)
                return BadRequest(new { message = "User already exists." });

            var token = Guid.NewGuid().ToString();
            _cache.Set(token, request, TimeSpan.FromMinutes(15));

            var link = Url.Action("ConfirmEmail", "Auth", new { token }, Request.Scheme);

            await _emailService.SendEmailAsync(
                request.Email,
                "Confirm your GameInventory account",
                $"Please click this link to confirm your account:\n\n{link}"
            );

            return Ok(new { message = "Confirmation email sent. Please verify to complete registration." });
        }

        // ── CONFIRM EMAIL ───────────────────────────────────────────────────────────
        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail(string token)
        {
            if (!_cache.TryGetValue<RegisterRequest>(token, out var request))
                return BadRequest(new { message = "Invalid or expired confirmation link." });

            if (await _userManager.FindByEmailAsync(request.Email) != null)
                return BadRequest(new { message = "User already exists." });

            var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
            var result = await _userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
                return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

            var confirmToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            await _userManager.ConfirmEmailAsync(user, confirmToken);

            _cache.Remove(token);

            return Ok(new { message = "Email confirmed and account created!" });
        }

        // ── LOGIN ───────────────────────────────────────────────────────────────────
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new
                {
                    errors = ModelState.Values.SelectMany(v => v.Errors)
                                               .Select(e => e.ErrorMessage)
                });

            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return Unauthorized(new
                {
                    message = "Account does not exist. If you created one, please confirm your email."
                });

            if (!await _userManager.IsEmailConfirmedAsync(user))
                return Unauthorized(new
                {
                    message = "Email not confirmed. Please check your inbox."
                });

            var signIn = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
            if (!signIn.Succeeded)
                return Unauthorized(new { message = "Incorrect password." });

            var jwt = new TokenService(_config).GenerateJwtToken(user);
            return Ok(new { token = jwt });
        }

        // ── FORGOT PASSWORD ──────────────────────────────────────────────────────────
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user != null)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var encoded = WebUtility.UrlEncode(token);
                var resetUrl = Url.Action("ResetPassword", "Auth", new
                {
                    token = encoded,
                    email = model.Email
                }, Request.Scheme);

                await _emailService.SendEmailAsync(
                    model.Email,
                    "Reset your GameInventory password",
                    $"Click here to reset your password:\n\n{resetUrl}"
                );
            }

            return Ok(new { message = "If that email exists, a reset link has been sent." });
        }

        // ── RESET PASSWORD (GET) – redirect to front-end ─────────────────────────────
        [HttpGet("reset-password")]
        public IActionResult ResetPasswordRedirect(string token, string email)
        {
            var frontendBase = "http://localhost:5173"; // TODO: move to config
            var url = $"{frontendBase}/reset-password?token={WebUtility.UrlEncode(token)}&email={WebUtility.UrlEncode(email)}";
            return Redirect(url);
        }

        // ── RESET PASSWORD (POST) – actual reset logic ────────────────────────────────
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return BadRequest(new { message = "Invalid request." });

            var decodedToken = WebUtility.UrlDecode(model.Token);
            var result = await _userManager.ResetPasswordAsync(user, decodedToken, model.NewPassword);

            if (!result.Succeeded)
                return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

            return Ok(new { message = "Password has been reset successfully." });
        }
    }
}
