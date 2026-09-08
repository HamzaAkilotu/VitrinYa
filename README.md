# VitrinYa

Küçük markaların ev, masaüstü ve hediye ürünlerini bir araya getiren Türkçe butik pazaryeri.

**ASP.NET Core MVC · .NET 10 · EF Core · SQL Server · ASP.NET Core Identity**

## Çalıştırma

.NET 10 SDK ve SQL Server gerekir. Windows geliştirme ortamında varsayılan bağlantı `(localdb)\MSSQLLocalDB` kullanır. SQL Server Express LocalDB kuruluysa sunucu adı veya kaynak kod değiştirmeden çalışır. LocalDB de SQL Server motorudur; sağlayıcı ve veritabanı şeması değişmez.

```powershell
dotnet restore VitrinYa.slnx
dotnet run --project VitrinYa.csproj --launch-profile http
```

Adres: **http://localhost:5100**. Development ortamında migration otomatik uygulanır ve örnek veriler eklenir. Sonraki açılışlarda mevcut veriler korunur. Demo veri oluşturma işlemi transaction ve başlangıç kilidi kullanır.

### Farklı SQL Server kullanmak

Kaynak kodu veya Git'te tutulan ayarları değiştirmeye gerek yok. Proje kökünde, Git tarafından yok sayılan `appsettings.Local.json` oluştur:

```json
{
  "ConnectionStrings": {
    "Shop": "Server=.;Database=VitrinYa;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False"
  }
}
```

`Server=.` varsayılan yerel SQL Server örneğidir; adlandırılmış örnek için kendi sunucu adını yaz. Linux/macOS üzerinde erişilebilir bir SQL Server kullan. LocalDB kurulumu uygulama tarafından yapılmaz.

Alternatif olarak `ConnectionStrings__Shop` ortam değişkenini kullan. Ortam değişkeni ve komut satırı yerel JSON ayarının önüne geçer. `appsettings.Local.json` yalnızca Development ortamında yüklenir ve publish çıktısına alınmaz. Parola içeren bağlantıları Git'e ekleme.

Production'da `ConnectionStrings__Shop` sağlanmalı; migration dağıtımdan önce uygulanmalıdır. `TrustServerCertificate=True` yalnızca yerel geliştirme içindir. Canlıda geçerli sertifika kullan. Veritabanı verileri SQL Server tarafından, yerel oturum anahtarları `App_Data/keys` altında saklanır.

SSMS ile kullandığın sunucuya bağlanıp `VitrinYa` veritabanındaki tabloları inceleyebilirsin.

## Demo hesaplar

Yalnızca Development ortamında oluşturulur. Ortak şifre: **VitrinYa!2026**.

| Rol | E-posta | Yetkiler |
| --- | --- | --- |
| Admin | admin@vitrinya.local | Kullanıcı, rol, kategori, ürün, koleksiyon ve sipariş yönetimi |
| Editör | editor@vitrinya.local | Ürün düzenleme, inceleme ve koleksiyonlar |
| Satıcı | satici@vitrinya.local | Form Atölye ürünleri, stokları ve satışları |
| İkinci satıcı | satici2@vitrinya.local | Sade Studio ürünleri, stokları ve satışları |
| Kullanıcı | kullanici@vitrinya.local | Alışveriş, favoriler, sepet ve kişisel siparişler |

Kayıt ekranı sadece Kullanıcı rolü oluşturur. Diğer rolleri admin atar. Ödeme ve kargo simülasyondur; kart bilgisi istenmez ve gerçek ödeme alınmaz.

## Mimari

Akış: **Controller → servis → EF Core / Identity → SQL Server**.

Controllerlar HTTP doğrulaması, yetki filtreleri, görünüm ve yönlendirmeden sorumludur. Sorgular, iş kuralları ve transactionlar servislerdedir. Servisler `IActionResult`, `ViewData`, `TempData` veya `ModelState` kullanmaz. EF Core üzerine ek repository, Unit of Work veya CQRS katmanı eklenmemiştir.

| Dosya / klasör | Sorumluluk |
| --- | --- |
| Program.cs | Yapılandırma, bağımlılıklar, güvenlik ve başlangıç |
| Services/CatalogService.cs | Vitrin, filtreler, ürün detayları ve favoriler |
| Services/CartService.cs | Sepet sorguları ve miktar değişiklikleri |
| Services/CheckoutService.cs | Atomik sipariş, stok düşümü ve tekrar gönderim kontrolü |
| Services/ProductService.cs | Panel özeti, ürün sahipliği, yayın süreci ve stok |
| Services/OrderService.cs | Kişisel siparişler, satıcı satışları ve sipariş aşamaları |
| Services/CollectionService.cs | Koleksiyon yönetimi |
| Services/AccountService.cs | Giriş, kayıt ve çıkış |
| Services/AdminService.cs | Kullanıcı, rol ve kategori yönetimi |
| Services/CurrentUser.cs | Kimliği doğrulanmış kullanıcının sunucu tarafındaki kimlik ve rolleri |
| Controllers/ServiceExceptionFilter.cs | Servis hatalarının HTTP yanıtlarına çevrilmesi |
| Models | Veri modelleri, form doğrulamaları ve görünüm modelleri |
| Data | İlişkiler, indexler, SQL kilitleri, migration ve örnek veri |
| Views / wwwroot | Razor ekranları, ortak bileşenler, CSS ve JavaScript |
| tests/VitrinYa.Tests | SQL Server entegrasyon ve servis doğrulama testleri |

## İş kuralları ve veri bütünlüğü

- Ürün akışı: **Taslak → İncelemede → Yayında**. Editör düzeltme isteyebilir. Ürün bilgisi/fiyatı düzenlenince yeniden inceleme gerekir; ayrı stok güncellemesi yayın durumunu korur.
- Satıcılar sadece kendi ürünlerini ve karma siparişlerde kendilerine ait kalemleri görür. Sipariş detaylarına müşteri veya admin erişir.
- Ürünler kalıcı silinmez, kullanıcılar pasifleştirilir. Ürün geçmişi olan kategori silinemez. Son aktif admin korunur.
- Fiyatlar tam sayı kuruş olarak saklanır. Siparişte ürün adı, satıcı ve satın alma fiyatı sabitlenir.
- Sepet/favori işlemleri ve ürün değişiklikleri SQL uygulama kilitleriyle koordine edilir. Kategori silme ile ürün kaydetme aynı kategori kilidini kullanır.
- Sipariş tek transaction içinde stok düşer, sipariş ve stok hareketi oluşturur, sepeti temizler. Hata halinde geri alınır. Koşullu SQL güncellemesi stok aşımını, benzersiz token çift siparişi önler.
- Sepet veya fiyat değişirse yeni özet için tekrar onay gerekir. Satışa kapanan ürün sepetten kaldırılabilir, miktarı artırılamaz.
- Rol değişikliği/pasifleştirme mevcut oturumları geçersizleştirir. Değiştiren isteklerde CSRF kontrolü ve yerel dönüş adresi kontrolü uygulanır.
- Katalog ve büyük listeler SQL üzerinde filtrelenir ve sayfalanır. Liste sorgularında `AsNoTracking`, gerekli ilişkilerde `AsSplitQuery` kullanılır. Kategori ekranı ürünleri yüklemek yerine SQL'de sayar. Favoriler kart başına sorgulanmaz.

## Testler

```powershell
dotnet test VitrinYa.slnx -c Release
```

Varsayılan test sunucusu `(localdb)\MSSQLLocalDB` olur. Farklı sunucu için:

```powershell
$env:VITRINYA_TEST_CONNECTION = 'Server=.;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test VitrinYa.slnx -c Release
```

Testler benzersiz `VitrinYa_Test_<guid>` veritabanları oluşturur; bitince yalnızca bu veritabanlarını siler. Mevcut mağaza veritabanı kullanılmaz. Test hesabının veritabanı oluşturma/silme yetkisi gerekir.

Kapsam: sayfa açılışları, rol ve sahiplik kontrolleri, CSRF, filtreleme, fiyat değişikliği, ürün yayın süreci, stok yarışları, sipariş tekrarı, rollback, sepet/favori eşzamanlılığı, rolü eksik hesabın onarılması ve servis doğrulaması.

## Şema değişikliği

```powershell
dotnet tool restore
dotnet ef migrations add MeaningfulChange --output-dir Data/Migrations
dotnet ef database update
```

Mevcut veritabanını silerek güncellemek yerine migration kullanılır.

## Sınırlar

Gerçek ödeme/kargo, dosya yükleme, iade, kupon, e-posta doğrulama ve şifre sıfırlama yoktur. Canlı kullanımda güvenli ilk admin oluşturma, HTTPS, kalıcı Data Protection anahtarları ve SQL yedekleme ayrıca yapılandırılmalıdır. Development demo hesaplarıyla internete açılmamalıdır. Ürün görselleri Unsplash, fontlar Google Fonts üzerinden gelir; çevrimdışı durumda yerel görsel ve sistem fontu yedeği kullanılır.
