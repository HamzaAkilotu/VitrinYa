using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VitrinYa.Models;
using VitrinYa.Services;
namespace VitrinYa.Controllers;

public class HomeController(CatalogService catalog, CurrentUser current) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = await catalog.HomeAsync();
        ViewData["FavoriteIds"] = await catalog.FavoriteIdsAsync(model.Products.Select(x => x.Id));
        return View(model);
    }
    [HttpGet]
    public async Task<IActionResult> Catalog([Bind("Search,CategoryId,CollectionId,SellerId,MinPrice,MaxPrice,Sort,Page,InStock,FavoritesOnly")] CatalogViewModel model)
    {
        if (model.FavoritesOnly && current.Id is null) return Challenge();
        if (!ModelState.IsValid || model.MinPrice is < 0 or > 1000000 || model.MaxPrice is < 0 or > 1000000 || model.MinPrice > model.MaxPrice)
            ViewData["FilterWarning"] = "Fiyat aralığı geçersizdi; geçerli sınırlarla güncellendi.";
        model = await catalog.SearchAsync(model);
        ModelState.Clear();
        ViewData["SellerName"] = model.SellerName;
        ViewData["Pager"] = PageInfo.Create(model.Page, model.Total, 12);
        ViewData["FavoriteIds"] = await catalog.FavoriteIdsAsync(model.Products.Select(x => x.Id));
        return View(model);
    }
    [Authorize, HttpPost]
    public async Task<IActionResult> SaveFavorite(int id, [BindRequired] bool save, string? returnUrl)
    {
        if (!ModelState.IsValid) return BadRequest();
        await catalog.SaveFavoriteAsync(id, save);
        TempData["Success"] = save ? "Güzel bir seçim daha favorilerine eklendi." : "Ürün favorilerinden çıkarıldı.";
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Catalog", new { FavoritesOnly = true });
    }
    [HttpGet]
    public async Task<IActionResult> Product(int id)
    {
        var details = await catalog.ProductAsync(id);
        if (details is null) return NotFound();
        ViewData["RelatedProducts"] = details.Related;
        ViewData["FavoriteIds"] = await catalog.FavoriteIdsAsync(details.Related.Select(x => x.Id).Append(id));
        return View(details.Product);
    }
    [IgnoreAntiforgeryToken]
    public IActionResult Status(int code)
    {
        Response.StatusCode = code is >= 400 and <= 599 ? code : 404;
        return View(new ErrorViewModel { StatusCode = Response.StatusCode });
    }
    [IgnoreAntiforgeryToken, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
