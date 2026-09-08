using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
namespace VitrinYa.Models;

public class LoginInput
{
    [Required, EmailAddress, Display(Name = "E-posta")] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), Display(Name = "Şifre")] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
public class RegisterInput : LoginInput
{
    [Required, StringLength(100), Display(Name = "Ad soyad")] public string DisplayName { get; set; } = "";
}
public class CatalogViewModel
{
    public List<Product> Products { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<Collection> Collections { get; set; } = [];
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public int? CollectionId { get; set; }
    public string? SellerId { get; set; }
    public string? SellerName { get; set; }
    public bool InStock { get; set; }
    public bool FavoritesOnly { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string Sort { get; set; } = "newest";
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public string Title { get; set; } = "Güzel şeyleri keşfet.";
}
public record PageInfo(int Current, int Total, int Size)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling((double)Total / Size));
    public int Offset => (Current - 1) * Size;
    public static PageInfo Create(int requested, int total, int size = 20)
        => new(Math.Clamp(requested, 1, Math.Max(1, (int)Math.Ceiling((double)total / size))), total, size);
}
public class CheckoutInput
{
    [Required, StringLength(100), Display(Name = "Teslim alacak kişi")] public string RecipientName { get; set; } = "";
    [Required, StringLength(500, MinimumLength = 10), Display(Name = "Teslimat adresi")] public string Address { get; set; } = "";
    public Guid Token { get; set; }
    [Required(ErrorMessage = "Sepet özetini yenileyip tekrar deneyin."), StringLength(64)]
    public string CartFingerprint { get; set; } = "";
}
public class CartViewModel
{
    public List<CartItem> Items { get; set; } = [];
    public CheckoutInput Checkout { get; set; } = new();
    public decimal Total => Items.Sum(x => x.Product.Price * x.Quantity);
    public bool CanCheckout => Items.Count > 0 && Items.All(x => !x.Product.IsDeleted && x.Product.Status == ProductStatus.Published && x.Product.Seller.IsActive && x.Quantity <= x.Product.Stock);
}
public class ProductInput
{
    public int Id { get; set; }
    public Guid Version { get; set; }
    [Required, StringLength(120), Display(Name = "Ürün adı")] public string Name { get; set; } = "";
    [Range(1, int.MaxValue), Display(Name = "Kategori")] public int CategoryId { get; set; }
    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true), Display(Name = "Fiyat (TL)")] public decimal Price { get; set; }
    [BindRequired, Range(0, 100000), Display(Name = "Stok")] public int Stock { get; set; }
    [Required, StringLength(3000), Display(Name = "Açıklama")] public string Description { get; set; } = "";
    [Required, StringLength(200), Display(Name = "Malzeme")] public string Material { get; set; } = "";
    [Required, StringLength(200), Display(Name = "Ölçüler")] public string Dimensions { get; set; } = "";
    [Required, StringLength(300), Display(Name = "Kutu içeriği")] public string BoxContents { get; set; } = "";
    [Range(1, 30), Display(Name = "Hazırlık süresi (gün)")] public int PreparationDays { get; set; } = 2;
    [Required, StringLength(1000), Display(Name = "Görsel URL (HTTPS)")] public string ImageUrl { get; set; } = "";
    [Display(Name = "Satıcı")] public string? SellerId { get; set; }
}
public class CollectionInput
{
    public int Id { get; set; }
    [Required, StringLength(80), Display(Name = "Koleksiyon adı")] public string Name { get; set; } = "";
    [StringLength(300), Display(Name = "Açıklama")] public string Description { get; set; } = "";
    public int[] ProductIds { get; set; } = [];
}
