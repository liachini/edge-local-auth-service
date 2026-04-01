using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using LocalAuthService.Data;

namespace LocalAuthService.Controllers;

public class AccountController : Controller
{
    private readonly AuthDbContext _db;

    public AccountController(AuthDbContext db)
    {
        _db = db;
    }

    [HttpGet("~/account/login")]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost("~/account/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == model.Username);

        if (user == null || !user.Enabled || !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError("", "Username o password non validi.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var identity = new ClaimsIdentity(claims, "LocalAuth");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("LocalAuth", principal);

        Console.WriteLine($"✅ User '{user.Username}' logged in via cookie");

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        return Redirect("/");
    }

    [HttpPost("~/account/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("LocalAuth");
        return Redirect("/account/login");
    }
}

public class LoginViewModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
