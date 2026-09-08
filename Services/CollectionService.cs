using Microsoft.EntityFrameworkCore;
using VitrinYa.Data;
using VitrinYa.Models;

namespace VitrinYa.Services;

public sealed class CollectionService(ShopDbContext db)
{
    public Task<List<Collection>> ListAsync() => db.Collections.AsNoTracking().Include(x => x.Products.Where(p => !p.IsDeleted)).AsSplitQuery().OrderBy(x => x.Name).ToListAsync();

    public Task<List<Product>> ProductsAsync() => db.Products.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();

    public async Task<CollectionInput?> GetAsync(int id)
    {
        if (id == 0) return new CollectionInput();
        var c = await db.Collections.Include(x => x.Products).SingleOrDefaultAsync(x => x.Id == id);
        return c is null ? null : new CollectionInput { Id = c.Id, Name = c.Name, Description = c.Description, ProductIds = c.Products.Select(x => x.Id).ToArray() };
    }

    public async Task SaveAsync(CollectionInput input)
    {
        InputValidation.Validate(input);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("collection:" + input.Id);
        var c = input.Id == 0 ? new Collection() : await db.Collections.Include(x => x.Products).SingleOrDefaultAsync(x => x.Id == input.Id);
        if (c is null) throw new ServiceException("", ServiceError.NotFound);
        c.Name = input.Name.Trim();
        c.Description = input.Description?.Trim() ?? "";
        c.Products = await db.Products.Where(x => !x.IsDeleted && input.ProductIds.Contains(x.Id)).ToListAsync();
        if (input.Id == 0) db.Collections.Add(c);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("collection:" + id);
        var c = await db.Collections.Include(x => x.Products).SingleOrDefaultAsync(x => x.Id == id);
        if (c is null) throw new ServiceException("", ServiceError.NotFound);
        c.Products.Clear();
        db.Collections.Remove(c);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
