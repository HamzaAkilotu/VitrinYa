using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VitrinYa.Data;
using VitrinYa.Models;
using VitrinYa.Services;

namespace VitrinYa.Tests;

public class ShopTests
{
    [Fact]
    public async Task Admin_can_find_and_repair_an_account_without_a_role()
    {
        using var app = new ShopFactory();
        var id = await app.WithDb(async db =>
        {
            var user = await db.Users.SingleAsync(x => x.Email == "kullanici@vitrinya.local");
            await db.UserRoles.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
            return user.Id;
        });
        using var admin = await Login(app, "admin@vitrinya.local");
        Assert.Contains("kullanici@vitrinya.local", await admin.GetStringAsync("/Admin/Users"));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Admin/UserEdit/" + id)).StatusCode);
        var response = await Post(admin, "/Admin/UserEdit", "/Admin/UserEdit/" + id, new()
        {
            ["Id"] = id, ["DisplayName"] = "Ece Yılmaz", ["Email"] = "kullanici@vitrinya.local",
            ["Role"] = Roles.Customer, ["IsActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, await app.WithDb(db => db.UserRoles.CountAsync(x => x.UserId == id)));
    }

    [Fact]
    public async Task Unavailable_cart_item_can_be_removed_but_not_increased()
    {
        using var app = new ShopFactory();
        var user = await AddCart(app, [1]);
        using var customer = await Login(app, "kullanici@vitrinya.local");
        var line = await app.WithDb(db => db.CartItems.SingleAsync(x => x.UserId == user));
        await app.WithDb(db => db.Products.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)));
        await Post(customer, "/Cart/Update", "/Cart", new() { ["id"] = line.Id.ToString(), ["quantity"] = "2" });
        Assert.Equal(1, await app.WithDb(db => db.CartItems.Select(x => x.Quantity).SingleAsync()));
        await Post(customer, "/Cart/Update", "/Cart", new() { ["id"] = line.Id.ToString(), ["quantity"] = "0" });
        Assert.False(await app.WithDb(db => db.CartItems.AnyAsync()));
    }

    [Fact]
    public async Task Review_waits_for_product_transaction_and_rejects_its_stale_version()
    {
        using var app = new ShopFactory();
        using var editor = await Login(app, "editor@vitrinya.local");
        await app.WithDb(db => db.Products.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ProductStatus.InReview)));
        var version = await app.WithDb(db => db.Products.Where(x => x.Id == 1).Select(x => x.Version).SingleAsync());
        var token = await Token(editor, "/Panel/Edit/1");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("product:1");
        var review = editor.PostAsync("/Panel/Review", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = "1", ["version"] = version.ToString(), ["publish"] = "true", ["__RequestVerificationToken"] = token
        }));
        await Assert.ThrowsAsync<TimeoutException>(() => review.WaitAsync(TimeSpan.FromMilliseconds(500)));
        await db.Products.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, Guid.NewGuid()));
        await transaction.CommitAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await review).StatusCode);
        Assert.Equal(ProductStatus.InReview, await app.WithDb(db => db.Products.Where(x => x.Id == 1).Select(x => x.Status).SingleAsync()));
    }

    [Theory]
    [InlineData("/Panel")]
    [InlineData("/Panel/Products")]
    [InlineData("/Panel/Edit")]
    [InlineData("/Panel/Edit/1")]
    [InlineData("/Panel/Stock")]
    [InlineData("/Panel/Orders")]
    [InlineData("/Panel/Collections")]
    [InlineData("/Panel/Collection/1")]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/UserEdit")]
    [InlineData("/Admin/Categories")]
    public async Task Admin_pages_render(string path)
    {
        using var app = new ShopFactory();
        using var client = await Login(app, "admin@vitrinya.local");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Catalog_applies_price_category_sort_and_collection_filters_in_sql()
    {
        using var app = new ShopFactory();
        using var client = app.Client();
        var html = await client.GetStringAsync("/Home/Catalog?CategoryId=1&MinPrice=300&MaxPrice=500&Sort=price-high");
        var ids = Regex.Matches(html, "href=\"/Home/Product/([0-9]+)\"").Select(x => x.Groups[1].Value).Distinct().ToArray();
        Assert.Equal(new[] { "6", "3" }, ids);
        html = await client.GetStringAsync("/Home/Catalog?CollectionId=2");
        ids = Regex.Matches(html, "href=\"/Home/Product/([0-9]+)\"").Select(x => x.Groups[1].Value).Distinct().ToArray();
        Assert.Contains("2", ids);
        Assert.DoesNotContain("1", ids);
    }

    [Fact]
    public async Task Sellers_only_see_their_own_lines_of_a_mixed_order()
    {
        using var app = new ShopFactory();
        var user = await AddCart(app, [1, 2]);
        var orderId = await app.Checkout(user);
        using var seller = await Login(app, "satici@vitrinya.local");
        var html = WebUtility.HtmlDecode(await seller.GetStringAsync("/Panel/Orders"));
        Assert.Contains("Dalga Seramik Tabak", html);
        Assert.DoesNotContain("Minimal Metal Masa Lambası", html);
        Assert.Equal(HttpStatusCode.NotFound, (await seller.GetAsync($"/Orders/Details/{orderId}")).StatusCode);
    }

    [Fact]
    public async Task Admin_manages_categories_and_editor_manages_collections()
    {
        using var app = new ShopFactory();
        using var admin = await Login(app, "admin@vitrinya.local");
        using var editor = await Login(app, "editor@vitrinya.local");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, "/Admin/SaveCategory", "/Admin/Categories", new() { ["name"] = "Test Kategori" })).StatusCode);
        var id = await app.WithDb(db => db.Categories.Where(x => x.Name == "Test Kategori").Select(x => x.Id).SingleAsync());
        var html = WebUtility.HtmlDecode(await admin.GetStringAsync("/"));
        Assert.Contains("Test Kategori", html);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(editor, "/Panel/Collection", "/Panel/Collection", new() { ["Name"] = "Test Koleksiyon", ["Description"] = "Özenli seçimler", ["ProductIds"] = "1" })).StatusCode);
        var collectionId = await app.WithDb(db => db.Collections.Where(x => x.Name == "Test Koleksiyon").Select(x => x.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await Post(editor, "/Panel/DeleteCollection", "/Panel/Collections", new() { ["id"] = collectionId.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, "/Admin/DeleteCategory", "/Admin/Categories", new() { ["id"] = id.ToString() })).StatusCode);
        Assert.False(await app.WithDb(db => db.Categories.AnyAsync(x => x.Id == id)));
        Assert.True(await app.WithDb(db => db.Products.AnyAsync(x => x.Id == 1)));
    }

    [Fact]
    public async Task Admin_order_status_can_only_advance_one_step()
    {
        using var app = new ShopFactory();
        var user = await AddCart(app, [1]);
        var id = await app.Checkout(user);
        using var admin = await Login(app, "admin@vitrinya.local");
        foreach (var status in new[] { "Tamamlandı", "Hazırlanıyor", "Tamamlandı" })
        {
            await Post(admin, "/Panel/UpdateOrder", "/Panel/Orders", new() { ["id"] = id.ToString(), ["status"] = status });
            if (status == "Hazırlanıyor") Assert.Equal(status, await app.WithDb(db => db.Orders.Where(x => x.Id == id).Select(x => x.Status).SingleAsync()));
        }
        Assert.Equal("Tamamlandı", await app.WithDb(db => db.Orders.Where(x => x.Id == id).Select(x => x.Status).SingleAsync()));
    }


    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    public async Task Malformed_stock_and_cart_quantity_never_mutate_data(string value)
    {
        using var app = new ShopFactory();
        using var seller = await Login(app, "satici@vitrinya.local");
        var product = await app.WithDb(db => db.Products.AsNoTracking().SingleAsync(x => x.Id == 1));
        var response = await Post(seller, "/Panel/UpdateStock", "/Panel/Products", new()
        {
            ["id"] = "1",
            ["version"] = product.Version.ToString(),
            ["stock"] = value
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(product.Stock, await app.WithDb(db => db.Products.Where(x => x.Id == 1).Select(x => x.Stock).SingleAsync()));
        var user = await AddCart(app, [1]);
        using var customer = await Login(app, "kullanici@vitrinya.local");
        var line = await app.WithDb(db => db.CartItems.SingleAsync(x => x.UserId == user));
        response = await Post(customer, "/Cart/Update", "/Cart", new() { ["id"] = line.Id.ToString(), ["quantity"] = value });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, await app.WithDb(db => db.CartItems.CountAsync()));
    }

    [Fact]
    public async Task Checkout_requires_new_confirmation_after_price_change()
    {
        using var app = new ShopFactory();
        var user = await AddCart(app, [1]);
        var input = Input();
        input.CartFingerprint = await app.WithDb(async db => CheckoutService.Fingerprint(await db.CartItems.Include(x => x.Product).ToListAsync()));
        await app.WithDb(db => db.Products.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.PriceCents, 80000L)));
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlaceOrderAsync(user, input));
        Assert.Contains("yeniden onaylayın", exception.Message);
        Assert.Equal(0, await app.WithDb(db => db.Orders.CountAsync()));
        Assert.Equal(12, await app.WithDb(db => db.Products.Where(x => x.Id == 1).Select(x => x.Stock).SingleAsync()));
        input.CartFingerprint = await app.WithDb(async db => CheckoutService.Fingerprint(await db.CartItems.Include(x => x.Product).ToListAsync()));
        await service.PlaceOrderAsync(user, input);
        Assert.Equal(80000L, await app.WithDb(db => db.Orders.Select(x => x.TotalCents).SingleAsync()));
    }


    [Fact]
    public async Task Favorites_are_idempotent_private_and_require_explicit_intent()
    {
        using var app = new ShopFactory();
        using var customer = await Login(app, "kullanici@vitrinya.local");
        using var other = await Login(app, "satici2@vitrinya.local");
        for (var i = 0; i < 2; i++)
        {
            var result = await Post(customer, "/Home/SaveFavorite", "/", new() { ["id"] = "1", ["save"] = "true", ["returnUrl"] = "https://example.com" });
            Assert.Equal("/Home/Catalog?FavoritesOnly=True", result.Headers.Location?.OriginalString);
        }
        Assert.Equal(1, await app.WithDb(db => db.Favorites.CountAsync()));
        Assert.Contains("/Home/Product/1", await customer.GetStringAsync("/Home/Catalog?FavoritesOnly=true"));
        Assert.DoesNotContain("/Home/Product/1", await other.GetStringAsync("/Home/Catalog?FavoritesOnly=true"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(customer, "/Home/SaveFavorite", "/", new() { ["id"] = "1" })).StatusCode);
        await Post(other, "/Home/SaveFavorite", "/", new() { ["id"] = "1", ["save"] = "false" });
        Assert.Equal(1, await app.WithDb(db => db.Favorites.CountAsync()));
        await app.WithDb(db => db.Products.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ProductStatus.Draft)));
        Assert.DoesNotContain("/Home/Product/1", await customer.GetStringAsync("/Home/Catalog?FavoritesOnly=true"));
        Assert.Equal(HttpStatusCode.NotFound, (await Post(other, "/Home/SaveFavorite", "/", new() { ["id"] = "1", ["save"] = "true" })).StatusCode);
        await Post(customer, "/Home/SaveFavorite", "/", new() { ["id"] = "1", ["save"] = "false" });
        Assert.Equal(0, await app.WithDb(db => db.Favorites.CountAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await app.Client().GetAsync("/Home/Catalog?FavoritesOnly=true")).StatusCode);
    }

    [Fact]
    public async Task Storefront_filters_stock_preserves_cents_and_hides_inactive_sellers()
    {
        using var app = new ShopFactory();
        using var client = app.Client();
        var seller = await app.WithDb(db => db.Products.Where(x => x.Id == 1).Select(x => x.SellerId).SingleAsync());
        await app.WithDb(db => db.Products.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.PriceCents, 34950L).SetProperty(x => x.Stock, 0)));
        var url = "/Home/Catalog?SellerId=" + seller;
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(url + "&Page=999"));
        Assert.Contains("349,50", html);
        Assert.Contains("/Home/Product/1", html);
        Assert.DoesNotContain("/Home/Product/2", html);
        Assert.DoesNotContain("/Home/Product/1", await client.GetStringAsync(url + "&InStock=true"));
        await app.WithDb(db => db.Users.Where(x => x.Id == seller).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Home/Product/1")).StatusCode);
    }

    [Fact]
    public async Task Checkout_form_refreshes_quote_before_accepting_changed_price()
    {
        using var app = new ShopFactory();
        await AddCart(app, [1]);
        using var client = await Login(app, "kullanici@vitrinya.local");
        static string Hidden(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
        var html = await client.GetStringAsync("/Cart");
        var oldFingerprint = Hidden(html, "Checkout.CartFingerprint");
        Assert.Equal(64, oldFingerprint.Length);
        var form = new Dictionary<string, string>
        {
            ["Checkout.Token"] = Hidden(html, "Checkout.Token"),
            ["Checkout.CartFingerprint"] = oldFingerprint,
            ["Checkout.RecipientName"] = "Test Alıcı",
            ["Checkout.Address"] = "Test Mahallesi No: 1 İstanbul"
        };
        await app.WithDb(db => db.Products.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.PriceCents, 70000L)));
        var response = await Post(client, "/Cart/Checkout", "/Cart", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        html = await response.Content.ReadAsStringAsync();
        Assert.Contains("yeniden onaylayın", WebUtility.HtmlDecode(html));
        Assert.Equal(0, await app.WithDb(db => db.Orders.CountAsync()));
        form["Checkout.CartFingerprint"] = Hidden(html, "Checkout.CartFingerprint");
        Assert.NotEqual(oldFingerprint, form["Checkout.CartFingerprint"]);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Cart/Checkout", "/Cart", form)).StatusCode);
        Assert.Equal(70000L, await app.WithDb(db => db.Orders.Select(x => x.TotalCents).SingleAsync()));
    }

    [Fact]
    public async Task Guest_post_login_returns_to_get_page_and_registration_retains_context()
    {
        using var app = new ShopFactory();
        using var guest = app.Client();
        var response = await Post(guest, "/Cart/Add", "/Account/Login", new() { ["productId"] = "1" });
        Assert.Contains("ReturnUrl=%2FCart", response.Headers.Location!.OriginalString);
        response = await Post(guest, "/Account/Register", "/Account/Register", new()
        {
            ["Email"] = "return@example.test",
            ["DisplayName"] = "Test Alıcı",
            ["Password"] = "TestPassword!42",
            ["ReturnUrl"] = "/Home/Product/1"
        });
        Assert.Equal("/Home/Product/1", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync(response.Headers.Location)).StatusCode);
    }

    [Fact]
    public async Task Unknown_route_keeps_404_with_friendly_page()
    {
        using var app = new ShopFactory();
        using var client = app.Client();
        var response = await client.GetAsync("/missing-page");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("VitrinYa", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Order_pagination_keeps_all_lines_of_each_order_together()
    {
        using var app = new ShopFactory();
        await app.WithDb(async db =>
        {
            var user = await db.Users.SingleAsync(x => x.Email == "kullanici@vitrinya.local");
            var products = await db.Products.Where(x => x.Id == 1 || x.Id == 2).ToListAsync();
            for (var i = 1; i <= 21; i++) db.Orders.Add(new Order
            {
                UserId = user.Id,
                CheckoutToken = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow.AddMinutes(i),
                RecipientName = "Test Alıcı",
                Address = "Test Mahallesi",
                TotalCents = products.Sum(x => x.PriceCents),
                Items = products.Select(p => new OrderItem { ProductId = p.Id, SellerId = p.SellerId, ProductName = $"Order-{i:00}-Product-{p.Id}", UnitPriceCents = p.PriceCents, Quantity = 1 }).ToList()
            });
            return await db.SaveChangesAsync();
        });
        using var admin = await Login(app, "admin@vitrinya.local");
        var first = await admin.GetStringAsync("/Panel/Orders");
        Assert.Contains("Order-02-Product-1", first);
        Assert.Contains("Order-02-Product-2", first);
        Assert.DoesNotContain("Order-01-Product-1", first);
        var last = await admin.GetStringAsync("/Panel/Orders?page=999");
        Assert.Contains("Order-01-Product-1", last);
        Assert.Contains("Order-01-Product-2", last);
        Assert.DoesNotContain("Order-02-Product-1", last);
        using var seller = await Login(app, "satici@vitrinya.local");
        last = await seller.GetStringAsync("/Panel/Orders?page=2");
        Assert.Contains("Order-01-Product-1", last);
        Assert.DoesNotContain("Order-01-Product-2", last);
    }


    [Fact]
    public async Task Concurrent_retries_create_one_order_and_one_stock_movement()
    {
        using var app = new ShopFactory();
        var user = await AddCart(app, [1]);
        var token = Guid.NewGuid();
        using var start = new Barrier(2);
        Task<int> Attempt() => Task.Run(async () =>
        {
            start.SignalAndWait(TimeSpan.FromSeconds(10));
            return await app.Checkout(user, token);
        });
        var ids = await Task.WhenAll(Attempt(), Attempt());
        Assert.Equal(ids[0], ids[1]);
        Assert.Equal(1, await app.WithDb(db => db.Orders.CountAsync()));
        Assert.Equal(1, await app.WithDb(db => db.StockTransactions.CountAsync(x => x.OrderId != null)));
        Assert.Equal(11, await app.WithDb(db => db.Products.Where(x => x.Id == 1).Select(x => x.Stock).SingleAsync()));
    }

    [Fact]
    public async Task Concurrent_cart_and_favorite_requests_do_not_duplicate_or_lose_updates()
    {
        using var app = new ShopFactory();
        using var first = await Login(app, "kullanici@vitrinya.local");
        using var second = await Login(app, "kullanici@vitrinya.local");
        var cartResults = await Task.WhenAll(
            Post(first, "/Cart/Add", "/Home/Product/1", new() { ["productId"] = "1" }),
            Post(second, "/Cart/Add", "/Home/Product/1", new() { ["productId"] = "1" }));
        Assert.All(cartResults, x => Assert.Equal(HttpStatusCode.Redirect, x.StatusCode));
        Assert.Equal(2, await app.WithDb(db => db.CartItems.Select(x => x.Quantity).SingleAsync()));
        var favoriteResults = await Task.WhenAll(
            Post(first, "/Home/SaveFavorite", "/", new() { ["id"] = "1", ["save"] = "true" }),
            Post(second, "/Home/SaveFavorite", "/", new() { ["id"] = "1", ["save"] = "true" }));
        Assert.All(favoriteResults, x => Assert.Equal(HttpStatusCode.Redirect, x.StatusCode));
        Assert.Equal(1, await app.WithDb(db => db.Favorites.CountAsync()));
    }

    [Fact]
    public async Task Locking_one_product_does_not_block_checkout_of_another_product()
    {
        using var app = new ShopFactory();
        var user = await AddCart(app, [2]);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockAsync("product:1");
        var order = await app.Checkout(user).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(order > 0);
        await transaction.RollbackAsync();
    }

    private sealed class ShopFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"VitrinYa_Test_{Guid.NewGuid():N}";
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Shop"] = (Environment.GetEnvironmentVariable("VITRINYA_TEST_CONNECTION") ?? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=True;Encrypt=True;TrustServerCertificate=True") + $";Database={databaseName};Pooling=False",
                ["Logging:LogLevel:Default"] = "Warning"
            }));
        }
        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        public async Task<T> WithDb<T>(Func<ShopDbContext, Task<T>> action)
        {
            using var scope = Services.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<ShopDbContext>());
        }
        public async Task<int> Checkout(string user, Guid? token = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var input = Input(token);
            input.CartFingerprint = CheckoutService.Fingerprint(await db.CartItems.Include(x => x.Product).Where(x => x.UserId == user).ToListAsync());
            db.ChangeTracker.Clear();
            return await scope.ServiceProvider.GetRequiredService<CheckoutService>().PlaceOrderAsync(user, input);
        }
        private bool disposed;
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
                if (db.Database.GetDbConnection().Database != databaseName || !databaseName.StartsWith("VitrinYa_Test_"))
                    throw new InvalidOperationException("Only isolated test databases may be deleted.");
                db.Database.EnsureDeleted();
            }
            base.Dispose(disposing);
        }
    }

    private static CheckoutInput Input(Guid? token = null) => new()
    {
        Token = token ?? Guid.NewGuid(),
        RecipientName = "Test Alıcı",
        Address = "Test Mahallesi, Test Sokak No: 1, İstanbul"
    };

    private static async Task<string> Token(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success, $"No CSRF token on {path}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, string tokenPage, Dictionary<string, string> values)
    {
        values["__RequestVerificationToken"] = await Token(client, tokenPage);
        return await client.PostAsync(path, new FormUrlEncodedContent(values));
    }

    private static async Task<HttpClient> Login(ShopFactory app, string email)
    {
        var client = app.Client();
        var result = await Post(client, "/Account/Login", "/Account/Login", new() { ["Email"] = email, ["Password"] = "VitrinYa!2026" });
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal("/Panel", result.Headers.Location?.OriginalString);
        return client;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Home/Catalog")]
    [InlineData("/Home/Product/1")]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task Public_pages_render(string path)
    {
        using var app = new ShopFactory();
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("kullanici", "/Orders", "/Panel/Products")]
    [InlineData("satici", "/Panel/Products", "/Admin/Users")]
    [InlineData("editor", "/Panel/Collections", "/Admin/Categories")]
    [InlineData("admin", "/Admin/Users", null)]
    public async Task Roles_get_appropriate_panels(string account, string allowed, string? denied)
    {
        using var app = new ShopFactory();
        using var client = await Login(app, $"{account}@vitrinya.local");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Panel")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(allowed)).StatusCode);
        if (denied is not null)
        {
            var response = await client.GetAsync(denied);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Denied", response.Headers.Location!.OriginalString);
        }
    }

    [Fact]
    public async Task Seller_cannot_read_or_mutate_another_sellers_product()
    {
        using var app = new ShopFactory();
        using var client = await Login(app, "satici@vitrinya.local");
        var other = await app.WithDb(db => db.Products.AsNoTracking().SingleAsync(x => x.Id == 2));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Panel/Edit/2")).StatusCode);
        foreach (var action in new[] { "Edit", "Delete", "Submit", "UpdateStock" })
        {
            var response = await Post(client, $"/Panel/{action}", "/Panel/Edit/1",
                new() { ["Id"] = "2", ["Version"] = other.Version.ToString(), ["Stock"] = "999", ["Name"] = "Tampered" });
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Conflict });
        }
        var forbidden = await Post(client, "/Panel/Review", "/Panel/Edit/1",
            new() { ["Id"] = "2", ["Version"] = other.Version.ToString(), ["publish"] = "true" });
        Assert.Contains("/Account/Denied", forbidden.Headers.Location!.OriginalString);
        var current = await app.WithDb(db => db.Products.AsNoTracking().SingleAsync(x => x.Id == 2));
        Assert.Equal(other.Version, current.Version);
        Assert.Equal(other.Stock, current.Stock);
        Assert.Equal(other.Name, current.Name);
        Assert.False(current.IsDeleted);
    }

    private static Dictionary<string, string> ProductForm(Product? p = null) => new()
    {
        ["Id"] = (p?.Id ?? 0).ToString(),
        ["Version"] = (p?.Version ?? Guid.Empty).ToString(),
        ["Name"] = "Çalışma Köşesi Test Ürünü",
        ["CategoryId"] = "2",
        ["Price"] = "349,50",
        ["Stock"] = "5",
        ["Description"] = "Özenle üretilmiş test ürünü açıklaması.",
        ["Material"] = "Ahşap",
        ["Dimensions"] = "20 x 10 cm",
        ["BoxContents"] = "1 ürün",
        ["PreparationDays"] = "2",
        ["ImageUrl"] = "https://images.unsplash.com/photo-1544816155-12df9643f363",
        ["Status"] = "Published",
        ["SellerId"] = "forged-seller"
    };

    [Fact]
    public async Task Publication_cycle_requires_editor_and_preserves_seller_ownership()
    {
        using var app = new ShopFactory();
        using var seller = await Login(app, "satici@vitrinya.local");
        using var editor = await Login(app, "editor@vitrinya.local");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(seller, "/Panel/Edit", "/Panel/Edit", ProductForm())).StatusCode);
        var p = await app.WithDb(db => db.Products.OrderByDescending(x => x.Id).FirstAsync());
        Assert.Equal(ProductStatus.Draft, p.Status);
        Assert.Equal(34950, p.PriceCents);
        Assert.NotEqual("forged-seller", p.SellerId);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client().GetAsync($"/Home/Product/{p.Id}")).StatusCode);

        async Task Reload() => p = await app.WithDb(db => db.Products.AsNoTracking().SingleAsync(x => x.Id == p.Id));
        async Task Submit()
        {
            Assert.Equal(HttpStatusCode.Redirect, (await Post(seller, "/Panel/Submit", $"/Panel/Edit/{p.Id}",
                new() { ["id"] = p.Id.ToString(), ["version"] = p.Version.ToString() })).StatusCode);
            await Reload();
        }
        await Submit();
        Assert.Equal(ProductStatus.InReview, p.Status);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(editor, "/Panel/Review", $"/Panel/Edit/{p.Id}",
            new() { ["id"] = p.Id.ToString(), ["version"] = p.Version.ToString(), ["publish"] = "false", ["note"] = "Ölçü bilgisini netleştirin." })).StatusCode);
        await Reload();
        Assert.Equal(ProductStatus.ChangesRequested, p.Status);
        Assert.Contains("Ölçü", p.ReviewNote);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(seller, "/Panel/Edit", $"/Panel/Edit/{p.Id}", ProductForm(p))).StatusCode);
        await Reload();
        await Submit();
        Assert.Equal(HttpStatusCode.Redirect, (await Post(editor, "/Panel/Review", $"/Panel/Edit/{p.Id}",
            new() { ["id"] = p.Id.ToString(), ["version"] = p.Version.ToString(), ["publish"] = "true" })).StatusCode);
        await Reload();
        Assert.Equal(ProductStatus.Published, p.Status);
        var search = await app.Client().GetStringAsync("/Home/Catalog?Search=" + Uri.EscapeDataString("ÇALIŞMA KÖŞESİ"));
        Assert.Contains($"/Home/Product/{p.Id}", search);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(seller, "/Panel/Edit", $"/Panel/Edit/{p.Id}", ProductForm(p))).StatusCode);
        await Reload();
        Assert.Equal(ProductStatus.Draft, p.Status);
    }

    private static async Task<string> AddCart(ShopFactory app, int[] ids, string email = "kullanici@vitrinya.local")
    {
        return await app.WithDb(async db =>
        {
            var user = await db.Users.SingleAsync(x => x.Email == email);
            db.CartItems.AddRange(ids.Select(id => new CartItem { ProductId = id, UserId = user.Id, Quantity = 1 }));
            await db.SaveChangesAsync();
            return user.Id;
        });
    }

    [Fact]
    public async Task Insufficient_second_item_rolls_back_first_item_order_ledger_and_cart()
    {
        using var app = new ShopFactory();
        var user = await AddCart(app, [1, 2]);
        var originalStock = await app.WithDb(async db =>
        {
            await db.Products.Where(x => x.Id == 2).ExecuteUpdateAsync(s => s.SetProperty(x => x.Stock, 0));
            return await db.Products.Where(x => x.Id == 1).Select(x => x.Stock).SingleAsync();
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Checkout(user));
        await app.WithDb(async db =>
        {
            Assert.Equal(originalStock, await db.Products.Where(x => x.Id == 1).Select(x => x.Stock).SingleAsync());
            Assert.Empty(await db.Orders.ToListAsync());
            Assert.Empty(await db.StockTransactions.Where(x => x.OrderId != null).ToListAsync());
            Assert.Equal(2, await db.CartItems.CountAsync(x => x.UserId == user));
            return true;
        });
    }

    [Fact]
    public async Task Concurrent_orders_for_last_item_allow_exactly_one_success()
    {
        using var app = new ShopFactory();
        var user1 = await AddCart(app, [1]);
        var user2 = await AddCart(app, [1], "satici2@vitrinya.local");
        await app.WithDb(async db => await db.Products.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.Stock, 1)));
        using var start = new Barrier(2);
        Task<bool> Attempt(string user) => Task.Run(async () =>
        {
            start.SignalAndWait(TimeSpan.FromSeconds(10));
            try { await app.Checkout(user); return true; }
            catch (InvalidOperationException) { return false; }
        });
        var results = await Task.WhenAll(Attempt(user1), Attempt(user2));
        Assert.Single(results, x => x);
        await app.WithDb(async db =>
        {
            Assert.Equal(0, await db.Products.Where(x => x.Id == 1).Select(x => x.Stock).SingleAsync());
            Assert.Single(await db.Orders.ToListAsync());
            Assert.Single(await db.StockTransactions.Where(x => x.OrderId != null).ToListAsync());
            Assert.Single(await db.CartItems.ToListAsync());
            return true;
        });
    }

    [Fact]
    public async Task Retried_checkout_is_idempotent()
    {
        using var app = new ShopFactory();
        var user = await AddCart(app, [1]);
        var token = Guid.NewGuid();
        var first = await app.Checkout(user, token);
        var second = await app.Checkout(user, token);
        Assert.Equal(first, second);
        Assert.Equal(1, await app.WithDb(db => db.Orders.CountAsync()));
        Assert.Equal(11, await app.WithDb(db => db.Products.Where(x => x.Id == 1).Select(x => x.Stock).SingleAsync()));
    }

    private sealed class FailOrderSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<Order>().Any(x => x.State == EntityState.Added))
                throw new DbUpdateException("Injected database failure");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Database_failure_after_stock_update_rolls_back_everything()
    {
        using var app = new ShopFactory();
        var user = await AddCart(app, [1]);
        var connection = await app.WithDb(db => Task.FromResult(db.Database.GetConnectionString()));
        await using var failingDb = new ShopDbContext(new DbContextOptionsBuilder<ShopDbContext>()
            .UseSqlServer(connection).AddInterceptors(new FailOrderSave()).Options);
        var input = Input();
        input.CartFingerprint = CheckoutService.Fingerprint(await failingDb.CartItems.Include(x => x.Product).Where(x => x.UserId == user).ToListAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => new CheckoutService(failingDb).PlaceOrderAsync(user, input));
        await app.WithDb(async db =>
        {
            Assert.Equal(12, await db.Products.Where(x => x.Id == 1).Select(x => x.Stock).SingleAsync());
            Assert.Equal(0, await db.Orders.CountAsync());
            Assert.Equal(0, await db.StockTransactions.CountAsync(x => x.OrderId != null));
            Assert.Equal(1, await db.CartItems.CountAsync());
            return true;
        });
    }

    [Fact]
    public async Task Orders_are_private_and_posts_require_csrf()
    {
        using var app = new ShopFactory();
        var user = await AddCart(app, [1]);
        var orderId = await app.Checkout(user);
        using var owner = await Login(app, "kullanici@vitrinya.local");
        using var other = await Login(app, "satici2@vitrinya.local");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/Orders/Details/{orderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Orders/Details/{orderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/Cart/Add",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["productId"] = "1" }))).StatusCode);
    }

    [Fact]
    public async Task Admin_disable_invalidates_existing_session_and_blocks_login()
    {
        using var app = new ShopFactory();
        using var customer = await Login(app, "kullanici@vitrinya.local");
        using var admin = await Login(app, "admin@vitrinya.local");
        var user = await app.WithDb(db => db.Users.SingleAsync(x => x.Email == "kullanici@vitrinya.local"));
        var result = await Post(admin, "/Admin/UserEdit", $"/Admin/UserEdit/{user.Id}", new()
        {
            ["Id"] = user.Id,
            ["Email"] = user.Email!,
            ["DisplayName"] = user.DisplayName,
            ["Role"] = Roles.Customer,
            ["IsActive"] = "false"
        });
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Contains("/Account/Login", (await customer.GetAsync("/Orders")).Headers.Location!.OriginalString);
        var login = await Post(customer, "/Account/Login", "/Account/Login", new() { ["Email"] = user.Email!, ["Password"] = "VitrinYa!2026" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Giri", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Customer_cannot_promote_self_during_registration()
    {
        using var app = new ShopFactory();
        using var client = app.Client();
        var response = await Post(client, "/Account/Register", "/Account/Register", new()
        {
            ["Email"] = "new@example.test",
            ["Password"] = "TestPassword!42",
            ["DisplayName"] = "Test User",
            ["Role"] = Roles.Admin
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var role = await app.WithDb(db => (from u in db.Users
                                           join ur in db.UserRoles on u.Id equals ur.UserId
                                           join r in db.Roles on ur.RoleId equals r.Id
                                           where u.Email == "new@example.test"
                                           select r.Name).SingleAsync());
        Assert.Equal(Roles.Customer, role);
    }

    [Fact]
    public async Task Stock_edit_with_stale_version_cannot_overwrite_checkout()
    {
        using var app = new ShopFactory();
        using var seller = await Login(app, "satici@vitrinya.local");
        var version = await app.WithDb(db => db.Products.Where(x => x.Id == 1).Select(x => x.Version).SingleAsync());
        var user = await AddCart(app, [1]);
        await app.Checkout(user);
        var response = await Post(seller, "/Panel/UpdateStock", "/Panel/Products", new() { ["id"] = "1", ["version"] = version.ToString(), ["stock"] = "500" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(11, await app.WithDb(db => db.Products.Where(x => x.Id == 1).Select(x => x.Stock).SingleAsync()));
    }
}

