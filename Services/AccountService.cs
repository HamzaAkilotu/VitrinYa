using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VitrinYa.Data;
using VitrinYa.Models;

namespace VitrinYa.Services;

public sealed class AccountService(UserManager<AppUser> users, SignInManager<AppUser> signIn, ShopDbContext db)
{
    public async Task<bool> LoginAsync(LoginInput input)
    {
        var user = await users.FindByEmailAsync(input.Email.Trim());
        return user is not null && user.IsActive && (await signIn.PasswordSignInAsync(user, input.Password, false, lockoutOnFailure: true)).Succeeded;
    }

    public async Task RegisterAsync(RegisterInput input)
    {
        InputValidation.Validate(input);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new AppUser { UserName = input.Email.Trim(), Email = input.Email.Trim(), DisplayName = input.DisplayName.Trim() };
        var result = await users.CreateAsync(user, input.Password);
        if (result.Succeeded) result = await users.AddToRoleAsync(user, Roles.Customer);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw new ServiceException("Hesap oluşturulamadı. E-postanız kayıtlı olabilir. Şifre en az 8 karakter, büyük/küçük harf, rakam ve özel karakter içermeli.");
        }
        await transaction.CommitAsync();
        await signIn.SignInAsync(user, false);
    }

    public Task LogoutAsync() => signIn.SignOutAsync();

    public Task<AppUser?> FindAsync(string? id) => id is null ? Task.FromResult<AppUser?>(null) : users.FindByIdAsync(id);
}
