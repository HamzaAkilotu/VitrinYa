using VitrinYa.Models;

namespace VitrinYa.Services;

public sealed record ProductDetails(Product Product, List<Product> Related);
