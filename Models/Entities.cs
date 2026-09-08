using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace VitrinYa.Models;

public static class Roles
{
    public const string Admin = "Admin", Editor = "Editor", Seller = "Seller", Customer = "Customer";
    public static readonly string[] All = [Admin, Editor, Seller, Customer];
    public static string Label(string role) => role switch { Admin => "Admin", Editor => "Editör", Seller => "Satıcı", Customer => "Kullanıcı", _ => "Rol atanmamış" };
}
public class AppUser : IdentityUser
{
    [MaxLength(100)] public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public enum ProductStatus { Draft, InReview, Published, ChangesRequested }
public static class Labels
{
    public static string Turkish(this ProductStatus status) => status switch
    { ProductStatus.Draft => "Taslak", ProductStatus.InReview => "İncelemede", ProductStatus.Published => "Yayında", _ => "Düzeltme Gerekli" };
}
public class Category
{
    public int Id { get; set; }
    [Required, StringLength(60)] public string Name { get; set; } = "";
    public List<Product> Products { get; set; } = [];
}
public class Product
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    // Integer kuruş keeps price filtering, ordering and calculations exact.
    public long PriceCents { get; set; }
    [NotMapped] public decimal Price => PriceCents / 100m;
    public int Stock { get; set; }
    [MaxLength(3000)] public string Description { get; set; } = "";
    [MaxLength(200)] public string Material { get; set; } = "";
    [MaxLength(200)] public string Dimensions { get; set; } = "";
    [MaxLength(300)] public string BoxContents { get; set; } = "";
    public int PreparationDays { get; set; }
    [MaxLength(1000)] public string ImageUrl { get; set; } = "";
    public string SellerId { get; set; } = "";
    public AppUser Seller { get; set; } = null!;
    public ProductStatus Status { get; set; }
    [MaxLength(1000)] public string? ReviewNote { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<Collection> Collections { get; set; } = [];
}
public class Collection
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = "";
    [StringLength(300)] public string Description { get; set; } = "";
    public List<Product> Products { get; set; } = [];
}
public class CartItem
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
}
public class Favorite
{
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
}
public class Order
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public Guid CheckoutToken { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string RecipientName { get; set; } = "";
    [MaxLength(500)] public string Address { get; set; } = "";
    public string Status { get; set; } = "Alındı";
    public long TotalCents { get; set; }
    [NotMapped] public decimal Total => TotalCents / 100m;
    public List<OrderItem> Items { get; set; } = [];
}
public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string SellerId { get; set; } = "";
    public AppUser Seller { get; set; } = null!;
    public string ProductName { get; set; } = "";
    public long UnitPriceCents { get; set; }
    public int Quantity { get; set; }
}
public class StockTransaction
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int? OrderId { get; set; }
    public Order? Order { get; set; }
    public string ActorId { get; set; } = "";
    public AppUser Actor { get; set; } = null!;
    public int QuantityChange { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
