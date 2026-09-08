using System.ComponentModel.DataAnnotations;

namespace VitrinYa.Models;

public sealed record CategoryRow(int Id, string Name, int ProductCount);

public class DashboardViewModel
{
    public int ProductCount { get; set; }
    public int ReviewCount { get; set; }
    public int OrderCount { get; set; }
    public long RevenueCents { get; set; }
    public List<Product> Products { get; set; } = [];
}
public class UserRow
{
    public AppUser User { get; set; } = null!;
    public string Role { get; set; } = "";
}
public class UserInput
{
    public string? Id { get; set; }
    [Required, StringLength(100), Display(Name = "Ad soyad / Marka adı")] public string DisplayName { get; set; } = "";
    [Required, EmailAddress, Display(Name = "E-posta")] public string Email { get; set; } = "";
    [DataType(DataType.Password), Display(Name = "İlk giriş şifresi")] public string? Password { get; set; }
    [Required, Display(Name = "Rol")] public string Role { get; set; } = Roles.Customer;
    [Display(Name = "Hesap aktif")] public bool IsActive { get; set; } = true;
}

