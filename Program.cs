using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using VitrinYa.Data;
using VitrinYa.Models;
using VitrinYa.Services;

var builder = WebApplication.CreateBuilder(args);
// Personal machine settings stay untracked. Environment/CLI values take precedence.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables().AddCommandLine(args);
}
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var dataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataPath);
// Local development keys stay outside Git; production uses the platform key store.
if (builder.Environment.IsDevelopment()) builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys"))).SetApplicationName("VitrinYa");
builder.Services.AddDbContext<ShopDbContext>(o => o.UseSqlServer(
    builder.Configuration.GetConnectionString("Shop") ?? throw new InvalidOperationException("ConnectionStrings:Shop is required.")));

builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 8;
    o.User.RequireUniqueEmail = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<ShopDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/Denied";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    // A login must return to a GET page, never to a POST-only mutation endpoint.
    o.Events.OnRedirectToLogin = context =>
    {
        context.Response.Redirect(HttpMethods.IsPost(context.Request.Method)
            ? "/Account/Login?ReturnUrl=%2FCart" : context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.Filters.Add<VitrinYa.Controllers.ServiceExceptionFilter>();
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<CollectionService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<CheckoutService>();
var app = builder.Build();
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); app.UseHttpsRedirection(); }
app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "public,max-age=86400"
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    if (app.Environment.IsDevelopment()) await db.Database.MigrateAsync();
    await SeedData.InitializeAsync(scope.ServiceProvider, app.Environment.IsDevelopment());
}
app.Run();
public partial class Program { }
