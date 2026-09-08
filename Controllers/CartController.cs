using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VitrinYa.Models;
using VitrinYa.Services;
namespace VitrinYa.Controllers;

[Authorize]
public class CartController(CartService cart, CheckoutService checkout, CurrentUser current) : Controller
{
    private async Task<CartViewModel> Model(CheckoutInput? input = null)
    {
        var model = await cart.GetAsync(input);
        ModelState.Remove("Checkout.CartFingerprint");
        return model;
    }
    public async Task<IActionResult> Index() => View(await Model());
    [HttpPost]
    public async Task<IActionResult> Add(int productId, int quantity = 1)
    {
        if (!ModelState.IsValid || quantity is < 1 or > 99) return BadRequest("Geçerli bir adet girin.");
        try
        {
            await cart.AddAsync(productId, quantity);
            TempData["Success"] = "Ürün sepetinize eklendi.";
        }
        catch (ServiceException ex) when (ex.Error == ServiceError.Validation) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Index");
    }
    [HttpPost]
    public async Task<IActionResult> Update(int id, [BindRequired] int quantity)
    {
        if (!ModelState.IsValid || quantity is < 0 or > 99) return BadRequest("Geçerli bir adet girin.");
        try { await cart.UpdateAsync(id, quantity); }
        catch (ServiceException ex) when (ex.Error == ServiceError.Validation) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Index");
    }
    [HttpPost]
    public async Task<IActionResult> Checkout([Bind(Prefix = "Checkout")] CheckoutInput input)
    {
        if (!ModelState.IsValid) return View("Index", await Model(input));
        try
        {
            var id = await checkout.SubmitAsync(current.RequiredId, input);
            TempData["Success"] = "Siparişiniz alındı! Demo ödeme başarılı; kartınızdan ücret alınmadı.";
            return RedirectToAction("Details", "Orders", new { id });
        }
        catch (ServiceException ex) when (ex.Error == ServiceError.Validation) { ModelState.AddModelError(ex.Field, ex.Message); }
        catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        return View("Index", await Model(input));
    }
}
