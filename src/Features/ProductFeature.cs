using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using QRCodeApi.Common;

namespace QRCodeApi.Features;

public class ProductRepository
{
    private readonly ConcurrentDictionary<string, (string Name, string Sku, string Price, string Category)> _productDatabase = new();

    public ProductRepository()
    {
        _productDatabase["PROD-001"] = ("SteelSeries Arctis 5 Headset", "SKU-AUDIO-001", "$99.99", "Peripherals");
        _productDatabase["PROD-002"] = ("Keychron Q1 Pro Mechanical Keyboard", "SKU-KB-002", "$199.00", "Custom Keyboards");
    }

    public bool TryGetProduct(string id, out (string Name, string Sku, string Price, string Category) product) => _productDatabase.TryGetValue(id, out product);
}

public class ProductModule : IQrModule
{
    public void RegisterEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/product/create", CreateProduct).WithTags("3. Product Catalog API").WithSummary("Generates a stateless multi-use product tag");
        app.MapGet("/product/view/{id}", ViewProduct).RequireRateLimiting("BasicLimiter").WithTags("3. Product Catalog API").WithSummary("Client endpoint for routing to physical asset data");
    }

    private static IResult CreateProduct([FromQuery] string baseUrl, [FromQuery] string productId, IQrGeneratorService qr, ProductRepository repo)
    {
        string id = string.IsNullOrWhiteSpace(productId) || !repo.TryGetProduct(productId, out _) ? "PROD-001" : productId;
        repo.TryGetProduct(id, out var product);

        string productUrl = $"{baseUrl.TrimEnd('/')}/product/view/{id}";
        return Results.Ok(new { productId = id, productName = product.Name, qrBase64 = qr.GenerateQrBase64(productUrl), productUrl });
    }

    private static IResult ViewProduct(string id, HttpContext context, ProductRepository repo, IIdentitySecurityService security, IHtmlTemplateService html)
    {
        Console.WriteLine($"\n[AUDIT] Product Catalog Accessed at {DateTime.Now:T}\n  ├─ Product ID: {id}\n  ├─ Viewer:     {security.GetClientInfo(context)}");

        if (repo.TryGetProduct(id, out var product))
        {
            string detailsHtml = $"<p><strong>Product Name:</strong> {product.Name}</p><p><strong>SKU:</strong> {product.Sku}</p><p><strong>Price:</strong> {product.Price}</p><p><strong>Category:</strong> {product.Category}</p>";
            return Results.Content(html.RenderMobileView("Product Information", "OFFICIAL CATALOG", "#38bdf8", detailsHtml), "text/html");
        }
        return Results.Content(html.RenderMobileView("Item Not Found", "UNRESOLVED SKU", "#ef4444", "<p>The requested product identifier does not exist.</p>"), "text/html");
    }
}
