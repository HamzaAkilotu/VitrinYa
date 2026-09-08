using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VitrinYa.Models;
using VitrinYa.Services;
namespace VitrinYa.Controllers;

public class AccountController(AccountService accounts) : Controller
{
    [HttpGet] public IActionResult Login(string? returnUrl) => View(new LoginInput { ReturnUrl = returnUrl });
    [HttpPost]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid) return View(input);
        if (await accounts.LoginAsync(input)) return ReturnTo(input.ReturnUrl);
        ModelState.AddModelError("", "Giriş yapılamadı. Bilgilerinizi kontrol edin veya biraz sonra tekrar deneyin.");
        return View(input);
    }
    [HttpGet] public IActionResult Register(string? returnUrl) => View(new RegisterInput { ReturnUrl = returnUrl });
    [HttpPost]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        if (!ModelState.IsValid) return View(input);
        try { await accounts.RegisterAsync(input); }
        catch (ServiceException ex) when (ex.Error == ServiceError.Validation)
        {
            ModelState.AddModelError(ex.Field, ex.Message);
            return View(input);
        }
        return ReturnTo(input.ReturnUrl);
    }
    private IActionResult ReturnTo(string? url) => Url.IsLocalUrl(url) ? LocalRedirect(url!) : RedirectToAction("Index", "Panel");
    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        await accounts.LogoutAsync();
        return RedirectToAction("Index", "Home");
    }
    public IActionResult Denied() { Response.StatusCode = 403; return View(); }
}
