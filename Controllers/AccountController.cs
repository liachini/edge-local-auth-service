using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using LocalAuthService.Data;
using LocalAuthService.Models;
using LocalAuthService.Services;

namespace LocalAuthService.Controllers;

public class AccountController : Controller
{
    private readonly AuthDbContext _db;
    private readonly OperatingModeDetector _modeDetector;
    private readonly KeycloakAuthService _keycloakAuth;
    private readonly ILogger<AccountController> _logger;

    public AccountController(AuthDbContext db, OperatingModeDetector modeDetector, KeycloakAuthService keycloakAuth, ILogger<AccountController> logger)
    {
        _db = db;
        _modeDetector = modeDetector;
        _keycloakAuth = keycloakAuth;
        _logger = logger;
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

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == model.Username);

        _logger.LogInformation("LOGIN START for {Username}", model.Username);
        await _modeDetector.CheckAsync();
        _logger.LogInformation("Login attempt for {Username} — Keycloak online: {Online}", model.Username, _modeDetector.IsOnline);

        if (_modeDetector.IsOnline)
        {
            // Online: Keycloak è autoritativo
            var loginClientId = _keycloakAuth.GetLoginClientId();
            var keycloakOk = await _keycloakAuth.ValidateCredentialsAsync(model.Username!, model.Password!, loginClientId);
            _logger.LogInformation("Keycloak validation for {Username}: {Result}", model.Username, keycloakOk);

            if (!keycloakOk)
            {
                ModelState.AddModelError("", "Username o password non validi.");
                return View(model);
            }

            if (user == null)
            {
                user = new Models.User
                {
                    Username = model.Username!,
                    PasswordHash = "",
                    HasLocalPassword = false,
                    CreatedLocally = false
                };
                _db.Users.Add(user);
            }

            // Al primo login online salviamo subito la password locale per abilitare l'accesso offline
            if (!user.HasLocalPassword)
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);
                user.HasLocalPassword = true;
                user.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("Local password set for '{Username}' after first online login", model.Username);
            }

            await _db.SaveChangesAsync();
            await SignInUser(user);
            return RedirectToReturnUrl(model.ReturnUrl);
        }

        // Offline: fallback locale
        if (user == null || !user.Enabled)
        {
            ModelState.AddModelError("", "Username o password non validi.");
            return View(model);
        }

        if (!user.HasLocalPassword)
        {
            ModelState.AddModelError("", "Nessuna password locale impostata. Effettua almeno un login quando la connessione è disponibile.");
            return View(model);
        }

        if (!BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError("", "Username o password non validi.");
            return View(model);
        }

        await SignInUser(user);
        return RedirectToReturnUrl(model.ReturnUrl);
    }

    [HttpGet("~/account/set-local-password")]
    public IActionResult SetLocalPassword([FromQuery] string? returnUrl)
    {
        return View(new SetLocalPasswordViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost("~/account/set-local-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLocalPassword(SetLocalPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        if (model.Password != model.ConfirmPassword)
        {
            ModelState.AddModelError("", "Le password non coincidono.");
            return View(model);
        }

        // Legge l'utente dal cookie di sessione corrente
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return Redirect("/account/login");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);
        user.HasLocalPassword = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        Console.WriteLine($"✅ Local password set for user '{user.Username}'");

        return RedirectToReturnUrl(model.ReturnUrl);
    }

    [HttpPost("~/account/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("LocalAuth");
        return Redirect("/account/login");
    }

    private async Task SignInUser(Models.User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var identity = new ClaimsIdentity(claims, "LocalAuth");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("LocalAuth", principal, new AuthenticationProperties
        {
            IsPersistent = true
        });

        Console.WriteLine($"✅ User '{user.Username}' logged in");
    }

    private IActionResult RedirectToReturnUrl(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return Redirect("/");
    }
}

public class LoginViewModel
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ReturnUrl { get; set; }
}

