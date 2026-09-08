using Microsoft.EntityFrameworkCore;
using VitrinYa.Data;
using VitrinYa.Models;
using VitrinYa.Services;

namespace VitrinYa.Tests;

public class ServiceTests
{
    [Fact]
    public async Task Collection_service_validates_input_without_an_http_controller()
    {
        using var db = new ShopDbContext(new DbContextOptionsBuilder<ShopDbContext>().Options);
        var exception = await Assert.ThrowsAsync<ServiceException>(() => new CollectionService(db).SaveAsync(new CollectionInput { Name = " " }));
        Assert.Equal(ServiceError.Validation, exception.Error);
        Assert.Equal(nameof(CollectionInput.Name), exception.Field);
    }
}
