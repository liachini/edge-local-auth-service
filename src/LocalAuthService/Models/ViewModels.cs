namespace LocalAuthService.Models;

public class SetLocalPasswordViewModel
{
    public string? Password { get; set; }
    public string? ConfirmPassword { get; set; }
    public string? ReturnUrl { get; set; }
}
