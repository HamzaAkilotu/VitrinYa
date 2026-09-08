using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VitrinYa.Models;
using VitrinYa.Services;
namespace VitrinYa.Controllers;

[Authorize(Roles = Roles.Admin)]
public class AdminController(AdminService admin) : Controller
{
    public async Task<IActionResult> Users(string? search, string? role, int page = 1)
    {
        var result = await admin.UsersAsync(search, role, page);
        ViewData["Pager"] = result.Pager;
        return View(result.Items);
    }
    [HttpGet]
    public async Task<IActionResult> UserEdit(string? id)
    {
        var input = await admin.UserAsync(id);
        return input is null ? NotFound() : View(input);
    }
    [HttpPost]
    public async Task<IActionResult> UserEdit(UserInput input)
    {
        if (!ModelState.IsValid) return View(input);
        try { await admin.SaveUserAsync(input); }
        catch (ServiceException ex) when (ex.Error == ServiceError.Validation)
        {
            ModelState.AddModelError(ex.Field, ex.Message);
            return View(input);
        }
        TempData["Success"] = "Kullanıcı ve rolü kaydedildi. Önceki oturumlar sonlandırıldı.";
        return RedirectToAction("Users");
    }
    public async Task<IActionResult> Categories() => View(await admin.CategoriesAsync());
    [HttpPost]
    public async Task<IActionResult> SaveCategory(int id, string name)
    {
        if (!ModelState.IsValid) return BadRequest();
        try { await admin.SaveCategoryAsync(id, name); TempData["Success"] = "Kategori kaydedildi."; }
        catch (ServiceException ex) when (ex.Error == ServiceError.Validation) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Categories");
    }
    [HttpPost]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        if (!ModelState.IsValid) return BadRequest();
        try { await admin.DeleteCategoryAsync(id); TempData["Success"] = "Kategori silindi."; }
        catch (ServiceException ex) when (ex.Error == ServiceError.Validation) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Categories");
    }
}
