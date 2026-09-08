using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using VitrinYa.Data;
using VitrinYa.Models;

namespace VitrinYa.Services;

public class CheckoutService(ShopDbContext db, ILogger<CheckoutService>? logger = null)
{
    public async Task<int> SubmitAsync(string userId, CheckoutInput input)
    {
        try { return await PlaceOrderAsync(userId, input);
    }
        catch (Exception ex) when (ex is DbUpdateException or SqlException)
        {
            logger?.LogWarning(ex, "Checkout rolled back for {UserId}", userId);
            throw new ServiceException("Sipariş tamamlanamadı. Hiçbir ödeme alınmadı; lütfen tekrar deneyin.");
        }
    }
    // Detect changes since the displayed summary. Prices still come exclusively from SQL.

    public static string Fingerprint(IEnumerable<CartItem> cart) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("|", cart.OrderBy(x => x.ProductId).Select(x =>
            FormattableString.Invariant($"{x.ProductId}:{x.Quantity}:{x.Product.PriceCents}"))))));

    public async Task<int> PlaceOrderAsync(string userId, CheckoutInput input)
    {
        if (input.Token == Guid.Empty) throw new InvalidOperationException("Sepet sayfasını yenileyip tekrar deneyin.");
        // Lock this cart before reading; different customers may check out concurrently.
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.LockAsync("cart:" + userId);
            var existing = await db.Orders.SingleOrDefaultAsync(x => x.CheckoutToken == input.Token && x.UserId == userId);
            if (existing is not null)
            {
                await transaction.CommitAsync();
                return existing.Id;
            }
            var productIds = await db.CartItems.Where(x => x.UserId == userId).OrderBy(x => x.ProductId).Select(x => x.ProductId).ToListAsync();
            foreach (var productId in productIds) await db.LockAsync("product:" + productId);
            var cart = await db.CartItems.Include(x => x.Product).ThenInclude(x => x.Seller).Where(x => x.UserId == userId).OrderBy(x => x.ProductId).ToListAsync();
            if (cart.Count == 0) throw new InvalidOperationException("Sepetiniz boş.");
            if (input.CartFingerprint != Fingerprint(cart))
                throw new InvalidOperationException("Sepetiniz veya ürün fiyatları değişti. Güncel özeti kontrol edip siparişinizi yeniden onaylayın.");
            var order = new Order { UserId = userId, CheckoutToken = input.Token, RecipientName = input.RecipientName, Address = input.Address };
            foreach (var line in cart)
            {
                var p = line.Product;
                if (line.Quantity is < 1 or > 99 || !p.Seller.IsActive) throw new InvalidOperationException("Sepetinizde artık satın alınamayan bir ürün var.");
                // Conditional SQL update prevents overselling, independently of the earlier read.
                var affected = await db.Products.Where(x => x.Id == p.Id && !x.IsDeleted && x.Status == ProductStatus.Published && x.Stock >= line.Quantity && x.Version == p.Version && x.Seller.IsActive)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Stock, x => x.Stock - line.Quantity).SetProperty(x => x.Version, Guid.NewGuid()));
                if (affected != 1) throw new InvalidOperationException($"{p.Name} için yeterli stok yok veya ürün yayından kaldırılmış. Sepetinizi güncelleyin.");
                order.Items.Add(new OrderItem { ProductId = p.Id, SellerId = p.SellerId, ProductName = p.Name, UnitPriceCents = p.PriceCents, Quantity = line.Quantity });
                order.TotalCents = checked(order.TotalCents + p.PriceCents * line.Quantity);
                db.StockTransactions.Add(new StockTransaction { ProductId = p.Id, Order = order, ActorId = userId, QuantityChange = -line.Quantity, Reason = "Sipariş" });
            }
            db.Orders.Add(order);
            db.CartItems.RemoveRange(cart);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return order.Id;
        }
        catch
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
