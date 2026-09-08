using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VitrinYa.Models;
using VitrinYa.Services;
namespace VitrinYa.Controllers;

[Authorize]
public class OrdersController(OrderService orders) : Controller
{
    public async Task<IActionResult> Index(int page = 1)
    {
        var result = await orders.ListAsync(page);
        ViewData["Pager"] = result.Pager;
        return View(result.Items);
    }
    public async Task<IActionResult> Details(int id)
    {
        var order = await orders.DetailsAsync(id);
        return order is null ? NotFound() : View(order);
    }
}
