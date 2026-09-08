using Microsoft.EntityFrameworkCore;
using VitrinYa.Data;
using VitrinYa.Models;

namespace VitrinYa.Services;

public sealed class OrderService(ShopDbContext db, CurrentUser current)
{
    private string UserId => current.RequiredId;

    public async Task<PagedResult<Order>> ListAsync(int page)
    {
        var query = db.Orders.AsNoTracking().Where(x => x.UserId == UserId);
        var pager = PageInfo.Create(page, await query.CountAsync());

        return new PagedResult<Order>(await query.Include(x => x.Items).AsSplitQuery().OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(pager.Offset).Take(pager.Size).ToListAsync(), pager);
    }

    public Task<Order?> DetailsAsync(int id) => db.Orders.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Seller)
        .SingleOrDefaultAsync(x => x.Id == id && (x.UserId == UserId || current.IsInRole(Roles.Admin)));

    public async Task<PagedResult<OrderItem>> SalesAsync(int page)
    {
        var admin = current.IsInRole(Roles.Admin);
        var query = db.Orders.AsNoTracking().Where(x => admin || x.Items.Any(i => i.SellerId == UserId));
        var pager = PageInfo.Create(page, await query.CountAsync());

        // Page orders first so one mixed order is never split across pages.
        var ids = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(pager.Offset).Take(pager.Size).Select(x => x.Id).ToListAsync();
        return new PagedResult<OrderItem>(await db.OrderItems.AsNoTracking().Include(x => x.Order)
            .Where(x => ids.Contains(x.OrderId) && (admin || x.SellerId == UserId))
            .OrderByDescending(x => x.Order.CreatedAt).ThenByDescending(x => x.OrderId).ThenBy(x => x.Id).ToListAsync(), pager);
    }

    public async Task AdvanceAsync(int id, string status)
    {
        var states = new[] { "Alındı", "Hazırlanıyor", "Tamamlandı" };
        if (!states.Contains(status)) throw new ServiceException("Geçersiz sipariş durumu.");
        var order = await db.Orders.FindAsync(id);
        if (order is null) throw new ServiceException("", ServiceError.NotFound);
        if (Array.IndexOf(states, status) != Array.IndexOf(states, order.Status) + 1)
        {
            throw new ServiceException("Sipariş yalnızca bir sonraki aşamaya taşınabilir.");
        }
        var changed = await db.Orders.Where(x => x.Id == id && x.Status == order.Status).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status));
        if (changed != 1) throw new ServiceException("Sipariş durumu değişti. Sayfayı yenileyin.");
    }
}
