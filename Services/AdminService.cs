using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VitrinYa.Data;
using VitrinYa.Models;

namespace VitrinYa.Services;

public sealed class AdminService(ShopDbContext db, UserManager<AppUser> users, CurrentUser current)
{
    public async Task<PagedResult<UserRow>> UsersAsync(string? search, string? role, int page)
    {
        var query = db.Users.AsNoTracking().Select(u => new UserRow
        {
            User = u,
            Role = (from ur in db.UserRoles
                    join r in db.Roles on ur.RoleId equals r.Id
                    where ur.UserId == u.Id
                    orderby r.Name
                    select r.Name).FirstOrDefault() ?? ""
        });
        if (Roles.All.Contains(role)) query = query.Where(x => x.Role == role);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = ShopDbContext.SearchFold(search.Trim());
            query = query.Where(x => x.User.DisplayName.ToLower().Replace("ı", "i").Contains(term) || x.User.Email!.ToLower().Replace("ı", "i").Contains(term));
        }
        var pager = PageInfo.Create(page, await query.CountAsync());

        var rows = await query.OrderBy(x => x.User.DisplayName).ThenBy(x => x.User.Id).Skip(pager.Offset).Take(pager.Size).ToListAsync();
        return new PagedResult<UserRow>(rows, pager);
    }

    public async Task<UserInput?> UserAsync(string? id)
    {
        if (id is null) return new UserInput();
        var u = await users.FindByIdAsync(id);
        if (u is null) return null;
        return new UserInput { Id = u.Id, DisplayName = u.DisplayName, Email = u.Email!, Role = (await users.GetRolesAsync(u)).FirstOrDefault() ?? Roles.Customer, IsActive = u.IsActive };
    }

    public async Task SaveUserAsync(UserInput input)
    {
        InputValidation.Validate(input);
        if (!Roles.All.Contains(input.Role)) throw new ServiceException("Geçerli bir rol seçin.", field: "Role");
        if (input.Id is null && string.IsNullOrEmpty(input.Password)) throw new ServiceException("Yeni hesap için şifre gerekli.", field: "Password");
        if (input.Id == current.RequiredId && (!input.IsActive || input.Role != Roles.Admin))
            throw new ServiceException("Kendi admin yetkinizi kaldıramaz veya hesabınızı pasifleştiremezsiniz.", field: "");

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("administration");
        var u = input.Id is null ? new AppUser() : await users.FindByIdAsync(input.Id);
        if (u is null) throw new ServiceException("", ServiceError.NotFound);
        var oldRoles = input.Id is null ? [] : await users.GetRolesAsync(u);
        if (oldRoles.Contains(Roles.Admin) && (!input.IsActive || input.Role != Roles.Admin))
        {
            var activeAdmins = await (from account in db.Users
                                      join membership in db.UserRoles on account.Id equals membership.UserId
                                      join role in db.Roles on membership.RoleId equals role.Id
                                      where account.IsActive && role.Name == Roles.Admin
                                      select account.Id).CountAsync();
            if (activeAdmins <= 1)
            {
                throw new ServiceException("Sistemde en az bir aktif admin kalmalı.", field: "");
            }
        }
        if (oldRoles.Contains(Roles.Seller) && input.Role != Roles.Seller && await db.Products.AnyAsync(x => x.SellerId == u.Id && !x.IsDeleted))
        {
            throw new ServiceException("Satıcı rolünü değiştirmeden önce satıcının ürünlerini kaldırın.", field: "");
        }
        u.DisplayName = input.DisplayName.Trim();
        u.Email = input.Email;
        u.UserName = input.Email;
        u.IsActive = input.IsActive;
        var result = input.Id is null ? await users.CreateAsync(u, input.Password!) : await users.UpdateAsync(u);
        if (result.Succeeded && oldRoles.Any()) result = await users.RemoveFromRolesAsync(u, oldRoles);
        if (result.Succeeded) result = await users.AddToRoleAsync(u, input.Role);
        if (result.Succeeded) result = await users.UpdateSecurityStampAsync(u);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw new ServiceException("Hesap kaydedilemedi. E-posta benzersiz olmalı; şifre en az 8 karakter, büyük/küçük harf, rakam ve özel karakter içermeli.", field: "");
        }
        await transaction.CommitAsync();
    }

    public Task<List<CategoryRow>> CategoriesAsync() => db.Categories.AsNoTracking().OrderBy(x => x.Name).Select(x => new CategoryRow(x.Id, x.Name, x.Products.Count)).ToListAsync();

    public async Task SaveCategoryAsync(int id, string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 2 or > 60)
        {
            throw new ServiceException("Kategori adı 2–60 karakter olmalı.");
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("administration");
        if (await db.Categories.AnyAsync(x => x.Name == name && x.Id != id))
        {
            throw new ServiceException("Bu kategori zaten var.");
        }
        var category = id == 0 ? new Category() : await db.Categories.FindAsync(id);
        if (category is null) throw new ServiceException("", ServiceError.NotFound);
        category.Name = name;
        if (id == 0) db.Categories.Add(category);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task DeleteCategoryAsync(int id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("administration");
        await db.LockAsync("category:" + id);
        if (await db.Products.AnyAsync(x => x.CategoryId == id))
        {
            throw new ServiceException("Ürün geçmişi bulunan kategori silinemez; adını güncelleyebilirsiniz.");
        }
        var category = await db.Categories.FindAsync(id);
        if (category is null) throw new ServiceException("", ServiceError.NotFound);
        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
