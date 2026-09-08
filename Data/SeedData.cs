using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VitrinYa.Models;

namespace VitrinYa.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services, bool demo)
    {
        var db = services.GetRequiredService<ShopDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("seed");
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role)) EnsureSucceeded(await roles.CreateAsync(new IdentityRole(role)));
        if (!demo) { await transaction.CommitAsync(); return; }
        var users = services.GetRequiredService<UserManager<AppUser>>();
        foreach (var (email, name, role) in new[] {
            ("admin@vitrinya.local", "VitrinYa Yönetim", Roles.Admin),
            ("editor@vitrinya.local", "Deniz Editör", Roles.Editor),
            ("satici@vitrinya.local", "Form Atölye", Roles.Seller),
            ("satici2@vitrinya.local", "Sade Studio", Roles.Seller),
            ("kullanici@vitrinya.local", "Ece Yılmaz", Roles.Customer) })
        {
            if (await users.FindByEmailAsync(email) is not null) continue;
            var user = new AppUser { UserName = email, Email = email, DisplayName = name, EmailConfirmed = true };
            var result = await users.CreateAsync(user, "VitrinYa!2026");
            EnsureSucceeded(result);
            EnsureSucceeded(await users.AddToRoleAsync(user, role));
        }
        if (await db.Categories.AnyAsync()) { await transaction.CommitAsync(); return; }
        var categories = new[] { "Ev & Yaşam", "Masaüstü", "Aydınlatma", "Hediye" }.Select(x => new Category { Name = x }).ToArray();
        db.Categories.AddRange(categories);
        var seller = (await users.FindByEmailAsync("satici@vitrinya.local"))!;
        var seller2 = (await users.FindByEmailAsync("satici2@vitrinya.local"))!;
        var specs = new[] {
            ("Dalga Seramik Tabak", 0, 69000L, "photo-1578749556568-bc2c40e68b61", "Seramik", "21 cm çap"),
            ("Minimal Metal Masa Lambası", 2, 129000L, "photo-1507473885765-e6ed057f782c", "Metal", "38 × 20 cm"),
            ("Yavaş Sabahlar Kupa", 0, 34000L, "photo-1514228742587-6b1558fcca3d", "Seramik", "300 ml"),
            ("Günlük Bez Çanta", 3, 42000L, "photo-1544816155-12df9643f363", "Pamuk", "38 × 42 cm"),
            ("Amber Kokulu Mum", 3, 29000L, "photo-1603006905003-be475563bc59", "Soya wax & cam", "180 g"),
            ("Mint Seramik Saksı", 0, 48000L, "photo-1485955900006-10f4d324d411", "Seramik", "16 × 16 cm"),
            ("Bulut Pamuk Yastık", 0, 55000L, "photo-1584100936595-c0654b55a2e2", "Pamuk", "45 × 45 cm"),
            ("Yeni Başlangıçlar Defteri", 1, 24000L, "photo-1531346878377-a5be20888e57", "Geri dönüştürülmüş kağıt", "A5 · 120 sayfa")
        };
        for (var i = 0; i < specs.Length; i++)
        {
            var (name, category, price, photo, material, size) = specs[i];
            db.Products.Add(new Product
            {
                Name = name,
                Category = categories[category],
                PriceCents = price,
                Stock = 12 + i,
                Description = "Gündelik anlara küçük bir güzellik katmak için tasarlandı. Sade çizgileri ve özenle seçilen malzemesiyle yaşam alanına sıcak bir dokunuş getirir. Küçük seriler halinde üretildiği için her parça kendine özgüdür.",
                Material = material,
                Dimensions = size,
                BoxContents = "1 adet ürün, koruyucu ambalaj ve bakım kartı",
                PreparationDays = 2,
                ImageUrl = $"https://images.unsplash.com/{photo}?auto=format&fit=crop&w=1000&q=85",
                SellerId = i % 2 == 0 ? seller.Id : seller2.Id,
                Status = ProductStatus.Published,
                CreatedAt = DateTime.UtcNow.AddDays(-i)
            });
        }
        await db.SaveChangesAsync();
        var products = await db.Products.ToListAsync();
        db.Collections.AddRange(
            new Collection { Name = "Yeni Ev Hediyeleri", Description = "Yeni başlangıçlara, anlamlı küçük dokunuşlar.", Products = products.Where(x => x.CategoryId == categories[0].Id || x.CategoryId == categories[3].Id).ToList() },
            new Collection { Name = "Çalışma Masanı Yenile", Description = "İlham veren bir köşe, daha güzel bir gün.", Products = products.Where(x => x.CategoryId == categories[1].Id || x.CategoryId == categories[2].Id).ToList() });
        db.StockTransactions.AddRange(products.Select(p => new StockTransaction { ProductId = p.Id, ActorId = p.SellerId, QuantityChange = p.Stock, Reason = "İlk stok" }));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(", ", result.Errors.Select(x => x.Description)));
    }
}

