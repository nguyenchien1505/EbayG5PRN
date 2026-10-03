using System.ComponentModel.DataAnnotations;

namespace EbayGroup5.Web.Models;

public sealed class LoginForm
{
    [Required, EmailAddress, StringLength(100)]
    public string Email { get; set; } = "";
    [Required, StringLength(128), DataType(DataType.Password)]
    public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterForm
{
    [Required, StringLength(100), Display(Name = "Display name")]
    public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(100)]
    public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
    public string Password { get; set; } = "";
}
