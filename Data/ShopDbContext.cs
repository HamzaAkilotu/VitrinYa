using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using VitrinYa.Models;

namespace VitrinYa.Data;

public class ShopDbContext : IdentityDbContext<AppUser>
{
    public ShopDbContext(DbContextOptions<ShopDbContext> options) : base(options) { }

    // Transaction-owned SQL locks coordinate only the same cart/product/resource across app instances.
    public async Task LockAsync(string resource)
    {
        if (Database.CurrentTransaction is null) throw new InvalidOperationException("A transaction is required.");
        await Database.ExecuteSqlInterpolatedAsync($@"
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=15000;
            IF @result < 0 THROW 51000, 'The operation is busy. Please retry.', 1;");
    }

    public static string SearchFold(string value) => value.ToLower(CultureInfo.GetCultureInfo("tr-TR")).Replace('ı', 'i');
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.UseCollation("Turkish_100_CI_AS");
        b.Entity<Category>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Product>().HasIndex(x => new { x.Status, x.IsDeleted, x.CategoryId, x.PriceCents });
        b.Entity<Product>().HasIndex(x => new { x.Status, x.IsDeleted, x.CreatedAt });
        b.Entity<Product>().HasIndex(x => new { x.SellerId, x.IsDeleted, x.Status });
        b.Entity<Product>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Product>().ToTable(t => { t.HasCheckConstraint("CK_Product_Stock", "Stock >= 0"); t.HasCheckConstraint("CK_Product_Price", "PriceCents > 0"); });
        b.Entity<Product>().HasOne(x => x.Category).WithMany(x => x.Products).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Product>().HasOne(x => x.Seller).WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CartItem>().HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();
        b.Entity<Favorite>().HasKey(x => new { x.UserId, x.ProductId });
        b.Entity<CartItem>().ToTable(t => t.HasCheckConstraint("CK_Cart_Quantity", "Quantity BETWEEN 1 AND 99"));
        b.Entity<Order>().HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Entity<Order>().HasIndex(x => x.CheckoutToken).IsUnique();
        b.Entity<OrderItem>().HasIndex(x => new { x.SellerId, x.OrderId });
        b.Entity<OrderItem>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrderItem>().HasOne(x => x.Seller).WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockTransaction>().HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockTransaction>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        foreach (var foreignKey in b.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        b.Entity<StockTransaction>().HasIndex(x => new { x.ProductId, x.CreatedAt });
    }
}
