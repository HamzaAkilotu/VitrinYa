using Microsoft.EntityFrameworkCore;
using VitrinYa.Data;
using VitrinYa.Models;

namespace VitrinYa.Services;

public sealed class CatalogService(ShopDbContext db, CurrentUser current)
{
    private string? UserId => current.Id;
    private IQueryable<Product> PublishedProducts() => db.Products.AsNoTracking()
        .Include(x => x.Seller).Include(x => x.Category)
        .Where(x => !x.IsDeleted && x.Status == ProductStatus.Published && x.Seller.IsActive);

    public Task<List<Category>> CategoriesAsync() => db.Categories.AsNoTracking().OrderBy(x => x.Id).ToListAsync();

    public async Task<HashSet<int>> FavoriteIdsAsync(IEnumerable<int> productIds)
    {
        var ids = productIds.Distinct().ToArray();
        return UserId is null ? [] : (await db.Favorites.Where(x => x.UserId == UserId && ids.Contains(x.ProductId)).Select(x => x.ProductId).ToListAsync()).ToHashSet();
    }

    public async Task<CatalogViewModel> HomeAsync()
    {
        var model = new CatalogViewModel
        {
            Products = await PublishedProducts().OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(8).ToListAsync(),
            Categories = await db.Categories.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Collections = await db.Collections.AsNoTracking().OrderBy(x => x.Id).Take(4).ToListAsync()
        };

        return model;
    }

    public async Task<CatalogViewModel> SearchAsync(CatalogViewModel model)
    {
        model.Search = string.IsNullOrWhiteSpace(model.Search) ? null : model.Search.Trim()[..Math.Min(model.Search.Trim().Length, 120)];
        model.MinPrice = model.MinPrice.HasValue ? Math.Clamp(model.MinPrice.Value, 0, 1000000) : null;
        model.MaxPrice = model.MaxPrice.HasValue ? Math.Clamp(model.MaxPrice.Value, 0, 1000000) : null;
        if (model.MinPrice > model.MaxPrice) (model.MinPrice, model.MaxPrice) = (model.MaxPrice, model.MinPrice);
        if (model.Sort is not ("newest" or "price-low" or "price-high")) model.Sort = "newest";

        model.Categories = await db.Categories.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        model.Collections = await db.Collections.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        if (model.CategoryId.HasValue && !model.Categories.Any(x => x.Id == model.CategoryId)) throw new ServiceException("", ServiceError.NotFound);
        if (model.CollectionId.HasValue && !model.Collections.Any(x => x.Id == model.CollectionId)) throw new ServiceException("", ServiceError.NotFound);
        if (!string.IsNullOrEmpty(model.SellerId))
        {
            var seller = await db.Users.AsNoTracking().Where(x => x.Id == model.SellerId && x.IsActive &&
                db.Products.Any(p => p.SellerId == x.Id && !p.IsDeleted && p.Status == ProductStatus.Published))
                .Select(x => x.DisplayName).SingleOrDefaultAsync();
            if (seller is null) throw new ServiceException("", ServiceError.NotFound);
            model.SellerName = seller;
        }

        var query = PublishedProducts();
        if (model.Search is not null)
        {
            var term = ShopDbContext.SearchFold(model.Search);
            query = query.Where(x => x.Name.ToLower().Replace("ı", "i").Contains(term) ||
                x.Description.ToLower().Replace("ı", "i").Contains(term) || x.Seller.DisplayName.ToLower().Replace("ı", "i").Contains(term));
        }
        if (model.CategoryId.HasValue) query = query.Where(x => x.CategoryId == model.CategoryId);
        if (model.CollectionId.HasValue) query = query.Where(x => x.Collections.Any(c => c.Id == model.CollectionId));
        if (!string.IsNullOrEmpty(model.SellerId)) query = query.Where(x => x.SellerId == model.SellerId);
        if (model.InStock) query = query.Where(x => x.Stock > 0);
        if (model.FavoritesOnly) query = query.Where(x => db.Favorites.Any(f => f.UserId == UserId && f.ProductId == x.Id));
        if (model.MinPrice.HasValue)
        {
            var min = (long)decimal.Ceiling(model.MinPrice.Value * 100);
            query = query.Where(x => x.PriceCents >= min);
        }
        if (model.MaxPrice.HasValue)
        {
            var max = (long)decimal.Floor(model.MaxPrice.Value * 100);
            query = query.Where(x => x.PriceCents <= max);
        }
        query = model.Sort switch
        {
            "price-low" => query.OrderBy(x => x.PriceCents).ThenBy(x => x.Id),
            "price-high" => query.OrderByDescending(x => x.PriceCents).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        };
        model.Total = await query.CountAsync();
        var pager = PageInfo.Create(model.Page, model.Total, 12);
        model.Page = pager.Current;

        model.Products = await query.Skip(pager.Offset).Take(pager.Size).ToListAsync();
        model.Title = model.FavoritesOnly ? "Biriktirdiğin güzel şeyler." :
            model.SellerName ??
            model.Collections.FirstOrDefault(x => x.Id == model.CollectionId)?.Name ??
            model.Categories.FirstOrDefault(x => x.Id == model.CategoryId)?.Name ??
            (model.MaxPrice == 500 ? "500 TL Altı" : "Güzel şeyleri keşfet.");

        return model;
    }

    public async Task SaveFavoriteAsync(int id, bool save)
    {
        var userId = current.RequiredId;
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync($"favorite:{UserId}:{id}");
        var favorite = await db.Favorites.SingleOrDefaultAsync(x => x.UserId == UserId && x.ProductId == id);
        if (save && favorite is null)
        {
            if (!await PublishedProducts().AnyAsync(x => x.Id == id)) throw new ServiceException("", ServiceError.NotFound);
            db.Favorites.Add(new Favorite { UserId = userId, ProductId = id });
        }
        else if (!save && favorite is not null) db.Favorites.Remove(favorite);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<ProductDetails?> ProductAsync(int id)
    {
        var product = await PublishedProducts().SingleOrDefaultAsync(x => x.Id == id);
        if (product is null) return null;
        var related = await PublishedProducts().Where(x => x.Id != id && x.Stock > 0 &&
            (x.CategoryId == product.CategoryId || x.SellerId == product.SellerId))
            .OrderByDescending(x => x.CategoryId == product.CategoryId).ThenByDescending(x => x.CreatedAt).Take(4).ToListAsync();
        return new ProductDetails(product, related);
    }
}
