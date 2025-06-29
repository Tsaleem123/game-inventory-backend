using System.ComponentModel.DataAnnotations;

namespace GameInventory.DTOs.Auth
{
    public class ForgotPasswordRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}