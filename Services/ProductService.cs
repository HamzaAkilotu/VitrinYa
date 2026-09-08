using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VitrinYa.Data;
using VitrinYa.Models;

namespace VitrinYa.Services;

public sealed class ProductService(ShopDbContext db, UserManager<AppUser> users, CurrentUser current)
{
    private string UserId => current.RequiredId;
    private bool IsReviewer => current.IsReviewer;
    private IQueryable<Product> ManagedProducts() => db.Products.Where(x => !x.IsDeleted && (IsReviewer || x.SellerId == UserId));

    public async Task<DashboardViewModel> DashboardAsync()
    {
        var model = new DashboardViewModel();
        if (current.IsInRole(Roles.Customer))
        {
            model.OrderCount = await db.Orders.CountAsync(x => x.UserId == UserId);
        }
        else
        {
            var products = ManagedProducts();
            model.ProductCount = await products.CountAsync();
            model.ReviewCount = await products.CountAsync(x => x.Status == ProductStatus.InReview);
            model.Products = await products.AsNoTracking().Include(x => x.Seller).Include(x => x.Category).OrderByDescending(x => x.CreatedAt).Take(5).ToListAsync();
            if (current.IsInRole(Roles.Admin) || current.IsInRole(Roles.Seller))
            {
                var lines = db.OrderItems.Where(x => current.IsInRole(Roles.Admin) || x.SellerId == UserId);
                model.OrderCount = await lines.Select(x => x.OrderId).Distinct().CountAsync();
                model.RevenueCents = await lines.SumAsync(x => x.UnitPriceCents * x.Quantity);
            }
        }
        return model;
    }

    public async Task<PagedResult<Product>> ListAsync(ProductStatus? status, string? search, int page)
    {
        var query = ManagedProducts();
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = ShopDbContext.SearchFold(search.Trim());
            query = query.Where(x => x.Name.ToLower().Replace("ı", "i").Contains(term));
        }

        var pager = PageInfo.Create(page, await query.CountAsync());

        return new PagedResult<Product>(await query.AsNoTracking().Include(x => x.Seller).Include(x => x.Category)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip(pager.Offset).Take(pager.Size).ToListAsync(), pager);
    }

    public Task<Product?> FindAsync(int id) => ManagedProducts().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);

    public async Task<List<AppUser>> SellersAsync() => (await users.GetUsersInRoleAsync(Roles.Seller)).Where(x => x.IsActive).OrderBy(x => x.DisplayName).ToList();

    public static ProductInput ToInput(Product p)
    {
        return new ProductInput
        {
            Id = p.Id,
            Version = p.Version,
            Name = p.Name,
            CategoryId = p.CategoryId,
            Price = p.Price,
            Stock = p.Stock,
            Description = p.Description,
            Material = p.Material,
            Dimensions = p.Dimensions,
            BoxContents = p.BoxContents,
            PreparationDays = p.PreparationDays,
            ImageUrl = p.ImageUrl,
            SellerId = p.SellerId
        };
    }

    public async Task<int> SaveAsync(ProductInput input)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("category:" + input.CategoryId);
        await db.LockAsync("product:" + input.Id);
        var p = input.Id == 0 ? new Product() : await ManagedProducts().SingleOrDefaultAsync(x => x.Id == input.Id);
        if (p is null) throw new ServiceException("", ServiceError.NotFound);
        InputValidation.Validate(input);
        if (input.Id != 0 && p.Version != input.Version)
            throw new ServiceException("Ürün veya stok başka bir işlemle değişti. Sayfayı yenileyip güncel bilgilerle tekrar deneyin.", field: "");
        if (!await db.Categories.AnyAsync(x => x.Id == input.CategoryId)) throw new ServiceException("Geçerli bir kategori seçin.", field: "CategoryId");
        if (!Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var image) || image.Scheme != Uri.UriSchemeHttps)
            throw new ServiceException("Görsel için geçerli bir HTTPS adresi girin.", field: "ImageUrl");
        if (input.Price != decimal.Round(input.Price, 2)) throw new ServiceException("Fiyat en fazla iki ondalık basamak içerebilir.", field: "Price");
        if (input.Id == 0)
        {
            p.SellerId = IsReviewer ? input.SellerId ?? "" : UserId;
            var seller = await users.FindByIdAsync(p.SellerId);
            if (seller is null || !seller.IsActive || !await users.IsInRoleAsync(seller, Roles.Seller))
                throw new ServiceException("Aktif bir satıcı seçin.", field: "SellerId");
        }
        var stockChange = input.Stock - p.Stock;
        p.Name = input.Name.Trim();
        p.CategoryId = input.CategoryId;
        p.PriceCents = (long)(input.Price * 100);
        p.Stock = input.Stock;
        p.Description = input.Description.Trim();
        p.Material = input.Material.Trim();
        p.Dimensions = input.Dimensions.Trim();
        p.BoxContents = input.BoxContents.Trim();
        p.PreparationDays = input.PreparationDays;
        p.ImageUrl = input.ImageUrl.Trim();
        p.Status = ProductStatus.Draft;
        p.Version = Guid.NewGuid();
        if (input.Id == 0) db.Products.Add(p);
        if (stockChange != 0) db.StockTransactions.Add(new StockTransaction { Product = p, ActorId = UserId, QuantityChange = stockChange, Reason = input.Id == 0 ? "İlk stok" : "Ürün düzenlemesi" });
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw new ServiceException("Ürün başka bir işlemle güncellendi. Güncel bilgileri kontrol edin.", ServiceError.Conflict);
        }
        return p.Id;
    }

    public async Task SubmitAsync(int id, Guid version)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("product:" + id);
        var changed = await ManagedProducts().Where(x => x.Id == id && x.Version == version
            && (x.Status == ProductStatus.Draft || x.Status == ProductStatus.ChangesRequested))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ProductStatus.InReview).SetProperty(x => x.Version, Guid.NewGuid()));
        if (changed != 1) throw new ServiceException("Ürün bulunamadı veya durumu değişti. Sayfayı yenileyin.", ServiceError.Conflict);
        await transaction.CommitAsync();
    }

    public async Task ReviewAsync(int id, Guid version, bool publish, string? note)
    {
        if (!publish && (string.IsNullOrWhiteSpace(note) || note.Length > 1000))
        {
            throw new ServiceException("Düzeltme için 1–1000 karakterlik bir açıklama yazın.");
        }
        var next = publish ? ProductStatus.Published : ProductStatus.ChangesRequested;
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("product:" + id);
        var changed = await ManagedProducts().Where(x => x.Id == id && x.Version == version && x.Status == ProductStatus.InReview && x.Seller.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, next).SetProperty(x => x.ReviewNote, publish ? null : note!.Trim()).SetProperty(x => x.Version, Guid.NewGuid()));
        if (changed != 1) throw new ServiceException("İnceleme durumu değişti. Sayfayı yenileyin.", ServiceError.Conflict);
        await transaction.CommitAsync();
    }

    public async Task DeleteAsync(int id, Guid version)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("product:" + id);
        var changed = await ManagedProducts().Where(x => x.Id == id && x.Version == version)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true).SetProperty(x => x.Status, ProductStatus.Draft).SetProperty(x => x.Version, Guid.NewGuid()));
        if (changed != 1) throw new ServiceException("Ürün bulunamadı veya değişti. Sayfayı yenileyin.", ServiceError.Conflict);
        await transaction.CommitAsync();
    }

    public async Task UpdateStockAsync(int id, Guid version, int stock)
    {
        if (stock is < 0 or > 100000) throw new ServiceException("Stok için 0–100000 arasında tam sayı girin.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("product:" + id);
        var p = await ManagedProducts().SingleOrDefaultAsync(x => x.Id == id && x.Version == version);
        if (p is null) throw new ServiceException("Stok başka bir işlemle değişti. Sayfayı yenileyin.", ServiceError.Conflict);
        var delta = stock - p.Stock;
        if (delta != 0)
        {
            p.Stock = stock;
            p.Version = Guid.NewGuid();
            db.StockTransactions.Add(new StockTransaction { ProductId = p.Id, ActorId = UserId, QuantityChange = delta, Reason = "Stok düzeltmesi" });
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
    }

    public async Task<PagedResult<StockTransaction>> StockAsync(int page)
    {
        var query = db.StockTransactions.AsNoTracking().Where(x => IsReviewer || x.Product.SellerId == UserId);
        var pager = PageInfo.Create(page, await query.CountAsync(), 30);

        return new PagedResult<StockTransaction>(await query.Include(x => x.Product).Include(x => x.Actor)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip(pager.Offset).Take(pager.Size).ToListAsync(), pager);
    }
}
