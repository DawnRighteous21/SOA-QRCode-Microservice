using Microsoft.AspNetCore.Mvc;
using QRCoder;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// In-memory databases
var ticketDatabase = new ConcurrentDictionary<string, string>();
var productDatabase = new ConcurrentDictionary<string, string>();

// Pre-load a product for the Product QR demo
productDatabase["PROD-001"] = "SteelSeries Arctis 5 Gaming Headphones";

// ==========================================
// 1. DIGITAL TICKET (EVENT CHECK-IN FLOW)
// ==========================================

// A. GENERATE TICKET
app.MapGet("/ticket/generate", ([FromQuery] string ngrokUrl) =>
{
    if (string.IsNullOrWhiteSpace(ngrokUrl))
        return Results.BadRequest("Provide your Ngrok URL (e.g., ?ngrokUrl=https://abc.ngrok-free.app)");

    // Authenticity: Generate secure UUID and store in our server memory
    string ticketId = Guid.NewGuid().ToString();
    ticketDatabase[ticketId] = "Pending";

    string verifyUrl = $"{ngrokUrl.TrimEnd('/')}/ticket/verify/{ticketId}";

    using var qrGenerator = new QRCodeGenerator();
    using var qrData = qrGenerator.CreateQrCode(verifyUrl, QRCodeGenerator.ECCLevel.M);
    using var qrCode = new PngByteQRCode(qrData);
    string base64Qr = Convert.ToBase64String(qrCode.GetGraphic(10));

    // Return HTML UI that automatically polls for updates
    string html = $@"
    <!DOCTYPE html>
    <html style='font-family: Arial; text-align: center; margin-top: 50px;'>
    <body>
        <h1>Event Check-in Demo</h1>
        <img src='data:image/png;base64,{base64Qr}' width='250'/>
        <p>Ticket ID: {ticketId}</p>
        <h2 id='status' style='color: orange;'>Status: Waiting for scan...</h2>

        <script>
            // Polling: Checks the server every 2 seconds
            setInterval(async () => {{
                let response = await fetch('/ticket/status/{ticketId}');
                if (response.ok) {{
                    let data = await response.json();
                    if (data.status === 'Checked-In') {{
                        document.getElementById('status').innerHTML = '<span style=""color: green;"">Checked-In Successfully!</span>';
                    }}
                }}
            }}, 2000);
        </script>
    </body>
    </html>";
    return Results.Content(html, "text/html");
});

// B. VERIFY TICKET (Scanned by Phone)
app.MapGet("/ticket/verify/{id}", (string id, HttpContext context) =>
{
    // Logging: Record the scan attempt and device info in the console
    Console.WriteLine($"[LOG] Scan attempt at {DateTime.Now} for Ticket: {id}");
    Console.WriteLine($"[LOG] Scanner Device: {context.Request.Headers.UserAgent}");

    string WrapHtml(string body) => $"<!DOCTYPE html><html><body style='text-align:center; font-family:sans-serif; margin-top:20vh'>{body}</body></html>";

    // Authenticity Check: Did we create this?
    if (!ticketDatabase.ContainsKey(id))
    {
        Console.WriteLine("[LOG] Result: REJECTED - Fake ticket.");
        return Results.Content(WrapHtml("<h1 style='color:red;'>❌ Fake or Invalid Ticket!</h1>"), "text/html");
    }

    // State Check: Is it already used?
    if (ticketDatabase[id] == "Checked-In")
    {
        Console.WriteLine("[LOG] Result: WARNING - Ticket already used.");
        return Results.Content(WrapHtml("<h1 style='color:orange;'>Ticket Already Used!</h1>"), "text/html");
    }

    // Update State
    ticketDatabase[id] = "Checked-In";
    Console.WriteLine("[LOG] Result: SUCCESS - Ticket checked in.");

    // Device Verification: Send success message to the scanning phone
    return Results.Content(WrapHtml("<h1 style='color:green;'>Entry Granted!</h1><p>Ticket marked as used.</p>"), "text/html");
});

// C. STATUS ENDPOINT (Used by laptop to see updates)
app.MapGet("/ticket/status/{id}", (string id) =>
{
    if (ticketDatabase.TryGetValue(id, out var status)) return Results.Ok(new { status });
    return Results.NotFound();
});


// ==========================================
// 2. PRODUCT QR (MULTI-USE FLOW)
// ==========================================

// A. GENERATE PRODUCT QR
app.MapGet("/product/generate", ([FromQuery] string ngrokUrl) =>
{
    if (string.IsNullOrWhiteSpace(ngrokUrl))
        return Results.BadRequest("Provide your Ngrok URL.");

    string productId = "PROD-001";
    string productUrl = $"{ngrokUrl.TrimEnd('/')}/product/view/{productId}";

    using var qrGenerator = new QRCodeGenerator();
    using var qrData = qrGenerator.CreateQrCode(productUrl, QRCodeGenerator.ECCLevel.M);
    using var qrCode = new PngByteQRCode(qrData);
    string base64Qr = Convert.ToBase64String(qrCode.GetGraphic(10));

    string html = $@"
    <!DOCTYPE html>
    <html style='font-family: Arial; text-align: center; margin-top: 50px;'>
    <body>
        <h1>Product QR Demo</h1>
        <img src='data:image/png;base64,{base64Qr}' width='250'/>
        <p>Scan to view product details.</p>
    </body>
    </html>";
    return Results.Content(html, "text/html");
});

// B. VIEW PRODUCT (Scanned by Phone)
app.MapGet("/product/view/{id}", (string id, HttpContext context) =>
{
    // Logging: Log the product view
    Console.WriteLine($"[LOG] Product {id} viewed at {DateTime.Now}");

    if (productDatabase.TryGetValue(id, out var productName))
    {
        string html = $@"<!DOCTYPE html><html><body style='text-align:center; font-family:sans-serif; margin-top:20vh'>
            <h1 style='color:blue;'>{productName}</h1>
            <p>Product ID: {id}</p>
            <p>This is a static, multi-use QR code. It can be scanned infinite times without changing state.</p>
            </body></html>";
        return Results.Content(html, "text/html");
    }
    return Results.NotFound("Product not found.");
});

app.Run();
