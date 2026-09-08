using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VitrinYa.Models;
using VitrinYa.Services;
namespace VitrinYa.Controllers;
using Microsoft.AspNetCore.Mvc.Rendering;
[Authorize]
public class PanelController(ProductService products, CatalogService catalog, CollectionService collections, OrderService orders, CurrentUser current) : Controller
{
    private const string Staff = Roles.Admin + "," + Roles.Editor + "," + Roles.Seller;
    private const string Reviewers = Roles.Admin + "," + Roles.Editor;
    public async Task<IActionResult> Index() => View(await products.DashboardAsync());
    [Authorize(Roles = Staff)]
    public async Task<IActionResult> Products(ProductStatus? status, string? search, int page = 1)
    {
        var result = await products.ListAsync(status, search, page);
        ViewBag.Status = status;
        ViewData["Pager"] = result.Pager;
        return View(result.Items);
    }
    private async Task PrepareProductForm(bool includeSellers)
    {
        ViewBag.Categories = new SelectList(await catalog.CategoriesAsync(), "Id", "Name");
        if (includeSellers) ViewBag.Sellers = new SelectList(await products.SellersAsync(), "Id", "DisplayName");
    }
    [Authorize(Roles = Staff), HttpGet]
    public async Task<IActionResult> Edit(int id = 0)
    {
        var product = id == 0 ? null : await products.FindAsync(id);
        if (id != 0 && product is null) return NotFound();
        ViewBag.Product = product;
        await PrepareProductForm(id == 0 && current.IsReviewer);
        return View(product is null ? new ProductInput() : ProductService.ToInput(product));
    }
    [Authorize(Roles = Staff), HttpPost]
    public async Task<IActionResult> Edit(ProductInput input)
    {
        // Check ownership even when the submitted form is malformed.
        var product = input.Id == 0 ? null : await products.FindAsync(input.Id);
        if (input.Id != 0 && product is null) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                var id = await products.SaveAsync(input);
                TempData["Success"] = "Ürün taslak olarak kaydedildi. Yayın için incelemeye gönderebilirsiniz.";
                return RedirectToAction("Edit", new { id });
            }
            catch (ServiceException ex) when (ex.Error == ServiceError.Validation) { ModelState.AddModelError(ex.Field, ex.Message); }
        }
        ViewBag.Product = product;
        await PrepareProductForm(input.Id == 0 && current.IsReviewer);
        return View(input);
    }
    [Authorize(Roles = Staff), HttpPost]
    public async Task<IActionResult> Submit(int id, Guid version)
    {
        if (!ModelState.IsValid) return BadRequest();
        await products.SubmitAsync(id, version);
        TempData["Success"] = "Ürün editör incelemesine gönderildi.";
        return RedirectToAction("Products");
    }
    [Authorize(Roles = Reviewers), HttpPost]
    public async Task<IActionResult> Review(int id, Guid version, [BindRequired] bool publish, string? note)
    {
        if (!ModelState.IsValid) return BadRequest();
        try { await products.ReviewAsync(id, version, publish, note); }
        catch (ServiceException ex) when (ex.Error == ServiceError.Validation)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction("Edit", new { id });
        }
        TempData["Success"] = publish ? "Ürün vitrinde yayınlandı." : "Düzeltme notu satıcıya iletildi.";
        return RedirectToAction("Products");
    }
    [Authorize(Roles = Staff), HttpPost]
    public async Task<IActionResult> Delete(int id, Guid version)
    {
        if (!ModelState.IsValid) return BadRequest();
        await products.DeleteAsync(id, version);
        TempData["Success"] = "Ürün kaldırıldı. Geçmiş sipariş ve stok kayıtları korundu.";
        return RedirectToAction("Products");
    }
    [Authorize(Roles = Staff), HttpPost]
    public async Task<IActionResult> UpdateStock(int id, Guid version, [BindRequired] int stock)
    {
        if (!ModelState.IsValid) return BadRequest();
        await products.UpdateStockAsync(id, version, stock);
        TempData["Success"] = "Stok güncellendi. Ürünün yayın durumu korundu.";
        return RedirectToAction("Products");
    }
    [Authorize(Roles = Staff)]
    public async Task<IActionResult> Stock(int page = 1)
    {
        var result = await products.StockAsync(page);
        ViewData["Pager"] = result.Pager;
        return View(result.Items);
    }
    [Authorize(Roles = Roles.Admin + "," + Roles.Seller)]
    public async Task<IActionResult> Orders(int page = 1)
    {
        var result = await orders.SalesAsync(page);
        ViewData["Pager"] = result.Pager;
        return View(result.Items);
    }
    [Authorize(Roles = Roles.Admin), HttpPost]
    public async Task<IActionResult> UpdateOrder(int id, string status)
    {
        if (!ModelState.IsValid) return BadRequest();
        try { await orders.AdvanceAsync(id, status); TempData["Success"] = "Sipariş durumu güncellendi."; }
        catch (ServiceException ex) when (ex.Error == ServiceError.Validation) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Orders");
    }
    [Authorize(Roles = Reviewers)]
    public async Task<IActionResult> Collections() => View(await collections.ListAsync());
    [Authorize(Roles = Reviewers), HttpGet]
    public async Task<IActionResult> Collection(int id = 0)
    {
        var input = await collections.GetAsync(id);
        if (input is null) return NotFound();
        ViewBag.Products = await collections.ProductsAsync();
        return View(input);
    }
    [Authorize(Roles = Reviewers), HttpPost]
    public async Task<IActionResult> Collection(CollectionInput input)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Products = await collections.ProductsAsync();
            return View(input);
        }
        await collections.SaveAsync(input);
        TempData["Success"] = "Koleksiyon kaydedildi. Yalnızca yayındaki ürünler vitrinde görünür.";
        return RedirectToAction("Collections");
    }
    [Authorize(Roles = Reviewers), HttpPost]
    public async Task<IActionResult> DeleteCollection(int id)
    {
        if (!ModelState.IsValid) return BadRequest();
        await collections.DeleteAsync(id);
        TempData["Success"] = "Koleksiyon kaldırıldı. Ürünler korundu.";
        return RedirectToAction("Collections");
    }
}
