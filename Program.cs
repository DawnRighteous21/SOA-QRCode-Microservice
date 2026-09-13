using Microsoft.AspNetCore.Mvc;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing.ImageSharp;

var builder = WebApplication.CreateBuilder(args);

// Add Swagger services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Enable Swagger UI for easy testing
app.UseSwagger();
app.UseSwaggerUI();

// 1. GENERATE ENDPOINT
app.MapPost("/generate", ([FromBody] string payload) =>
{
    if (string.IsNullOrWhiteSpace(payload)) return Results.BadRequest("Payload cannot be empty.");

    // Generate the QR code data
    using var qrGenerator = new QRCodeGenerator();
    using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
    
    // Output as a PNG byte array (Cross-platform, no Windows dependencies)
    using var qrCode = new PngByteQRCode(qrCodeData);
    byte[] imageBytes = qrCode.GetGraphic(10);
    
    return Results.File(imageBytes, "image/png");
});

// 2. SCAN ENDPOINT
app.MapPost("/scan", async (IFormFile file) =>
{
    if (file == null || file.Length == 0) return Results.BadRequest("Invalid or empty file.");

    // Read the uploaded image stream
    using var stream = file.OpenReadStream();
    using var image = await Image.LoadAsync<Rgba32>(stream);

    // Decode the QR code
    var reader = new BarcodeReader<Rgba32>();
    var result = reader.Decode(image);

    if (result == null) return Results.BadRequest("No QR code found in the provided image.");

    return Results.Ok(new { decodedText = result.Text });
}).DisableAntiforgery();

app.Run();