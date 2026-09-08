using Microsoft.EntityFrameworkCore;
using VitrinYa.Data;
using VitrinYa.Models;

namespace VitrinYa.Services;

public sealed class CartService(ShopDbContext db, CurrentUser current)
{
    private string UserId => current.RequiredId;

    public async Task<int> CountAsync() => current.Id is null ? 0 : await db.CartItems.Where(x => x.UserId == current.Id).SumAsync(x => x.Quantity);

    public async Task<CartViewModel> GetAsync(CheckoutInput? input = null)
    {
        var items = await db.CartItems.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.Seller)
            .Where(x => x.UserId == UserId).OrderBy(x => x.Id).ToListAsync();
        input ??= new CheckoutInput { Token = Guid.NewGuid() };
        input.CartFingerprint = CheckoutService.Fingerprint(items);

        return new CartViewModel { Items = items, Checkout = input };
    }

    public async Task AddAsync(int productId, int quantity)
    {
        if (quantity is < 1 or > 99) throw new ServiceException("Geçerli bir adet girin.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("cart:" + UserId);
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == productId && !x.IsDeleted && x.Status == ProductStatus.Published && x.Seller.IsActive);
        if (product is null) throw new ServiceException("", ServiceError.NotFound);
        var line = await db.CartItems.SingleOrDefaultAsync(x => x.UserId == UserId && x.ProductId == productId);
        var next = (line?.Quantity ?? 0) + quantity;
        if (next > product.Stock || next > 99)
        {
            throw new ServiceException("İstediğiniz miktar için yeterli stok yok. Sepetinizi kontrol edin.");
        }
        if (line is null) db.CartItems.Add(new CartItem { UserId = UserId, ProductId = productId, Quantity = quantity });
        else line.Quantity = next;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task UpdateAsync(int id, int quantity)
    {
        if (quantity is < 0 or > 99) throw new ServiceException("Geçerli bir adet girin.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("cart:" + UserId);
        var line = await db.CartItems.Include(x => x.Product).ThenInclude(x => x.Seller).SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (line is null) throw new ServiceException("", ServiceError.NotFound);
        if (quantity == 0) db.CartItems.Remove(line);
        else if (line.Product.IsDeleted || line.Product.Status != ProductStatus.Published || !line.Product.Seller.IsActive)
            throw new ServiceException("Bu ürün artık satın alınamıyor. Sepetinizden kaldırabilirsiniz.");
        else if (quantity > line.Product.Stock)
        {
            throw new ServiceException("Bu miktar için yeterli stok yok.");
        }
        else line.Quantity = quantity;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
