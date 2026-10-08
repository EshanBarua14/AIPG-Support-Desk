using System.ComponentModel.DataAnnotations;

namespace XFLCSMS.Models.Register
{
    public class Password
    {
        public int UserId { get; set; }
        public string? CurrentPassword { get; set; }
        [XFLCSMS.Services.StrongPassword]
        public string? NewPassword { get; set; }
        [Required]
        public string? ConNewPassword { get; set; }



    }
}
