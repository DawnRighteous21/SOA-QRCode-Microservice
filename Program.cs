using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QRCoder;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// SECURITY: Rate Limiting (Anti-DDoS)
// ==========================================
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("BasicLimiter", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(1);
        opt.PermitLimit = 10;
    });

    options.OnRejected = async (context, token) =>
    {
        Console.WriteLine($"\n[SECURITY ALERT] {DateTime.Now:T} - DDoS Protection triggered! Dropping requests.");
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsync("DDoS Protection: Too many requests. Please slow down.", token);
    };
});

var app = builder.Build();
app.UseRateLimiter();

// Enable serving the Dashboard from the wwwroot/index.html file
app.UseDefaultFiles();
app.UseStaticFiles();

// ==========================================
// IN-MEMORY DATABASES & CONFIG
// ==========================================
var checkinDatabase = new ConcurrentDictionary<string, string>();
var productDatabase = new ConcurrentDictionary<string, (string Name, string Sku, string Price, string Category)>();

// Security: Device Fingerprinting to prevent passback loops
var registeredDevices = new ConcurrentDictionary<string, DateTime>();

productDatabase["PROD-001"] = ("SteelSeries Arctis 5 Headset", "SKU-AUDIO-001", "$99.99", "Peripherals");
productDatabase["PROD-002"] = ("Keychron Q1 Pro Mechanical Keyboard", "SKU-KB-002", "$199.00", "Custom Keyboards");

// Server Secret Key for cryptographic signature verification
const string ServerSecretKey = "TDTU_SOA_Cryptographic_Signature_Key_2026!";

// ==========================================
// HELPER METHODS
// ==========================================
string GenerateQrBase64(string content)
{
    using var qrGenerator = new QRCodeGenerator();
    using var qrData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
    using var qrCode = new PngByteQRCode(qrData);
    return Convert.ToBase64String(qrCode.GetGraphic(10));
}

// Extract true IP and full string through the Ngrok proxy
string GetClientInfo(HttpContext context)
{
    var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString() ?? "Unknown IP";
    var agent = context.Request.Headers.UserAgent.ToString();
    // Fully unbounded User-Agent string output
    return $"IP: {ip}\n  └─ Device:     {agent}";
}

// Create a unique fingerprint based on device hardware/network
string GetDeviceFingerprint(HttpContext context)
{
    var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString() ?? "Unknown IP";
    var agent = context.Request.Headers.UserAgent.ToString();
    return $"{ip}::{agent}";
}

string RenderMobileView(string title, string statusBadgeText, string badgeColor, string detailsHtml)
{
    return $@"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{title}</title>
    <style>
        * {{ box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }}
        body {{ background-color: #0b0f19; color: #f1f5f9; display: flex; align-items: center; justify-content: center; min-height: 100vh; padding: 20px; }}
        .card {{ background: #131b2e; border: 1px solid #1e293b; border-radius: 16px; padding: 32px 24px; max-width: 420px; width: 100%; box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5); text-align: center; }}
        .badge {{ display: inline-block; padding: 6px 14px; border-radius: 9999px; font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 20px; background: {badgeColor}22; color: {badgeColor}; border: 1px solid {badgeColor}44; }}
        h1 {{ font-size: 22px; font-weight: 700; margin-bottom: 12px; color: #ffffff; }}
        .content {{ background: #0b0f19; border: 1px solid #1e293b; border-radius: 12px; padding: 18px; margin-top: 20px; text-align: left; font-size: 14px; line-height: 1.6; color: #94a3b8; }}
        .content strong {{ color: #e2e8f0; }}
        .footer {{ margin-top: 24px; font-size: 11px; color: #475569; text-transform: uppercase; letter-spacing: 1px; }}
    </style>
</head>
<body>
    <div class='card'>
        <div class='badge'>{statusBadgeText}</div>
        <h1>{title}</h1>
        <div class='content'>{detailsHtml}</div>
        <div class='footer'>SOA Microservices Verification Node</div>
    </div>
</body>
</html>";
}

// ==========================================
// 1. EVENT CHECK-IN API & VERIFICATION
// ==========================================

// JSON endpoint for dashboard
app.MapGet("/api/checkin/create", ([FromQuery] string baseUrl) =>
{
    string checkinId = Guid.NewGuid().ToString();
    checkinDatabase[checkinId] = "Pending";

    string verifyUrl = $"{baseUrl.TrimEnd('/')}/checkin/verify/{checkinId}";
    string qrBase64 = GenerateQrBase64(verifyUrl);

    return Results.Ok(new { checkinId, qrBase64, verifyUrl });
});

// Admin endpoint to reset the tracking for testing
app.MapPost("/api/checkin/reset", () =>
{
    checkinDatabase.Clear();
    registeredDevices.Clear();
    Console.WriteLine($"\n[ADMIN] {DateTime.Now:T} - Active Session Queue and Device Fingerprints Cleared.");
    return Results.Ok();
});

// Mobile verification endpoint
app.MapGet("/checkin/verify/{id}", (string id, HttpContext context) =>
{
    Console.WriteLine($"\n[AUDIT] Check-In Scan Attempt at {DateTime.Now:T}");
    Console.WriteLine($"  ├─ Session ID: {id}");
    Console.WriteLine($"  ├─ Scanner:    {GetClientInfo(context)}");

    string fingerprint = GetDeviceFingerprint(context);

    // SECURITY: Device Fingerprinting check
    if (registeredDevices.ContainsKey(fingerprint))
    {
        Console.WriteLine("  => RESULT:     [BLOCKED] (Device Already Registered in Event)");
        string blockHtml = "<p><strong>Result:</strong> Device Already Registered</p><p>You have already successfully scanned a queue ticket for this event. Please proceed.</p>";
        return Results.Content(RenderMobileView("Device Recognized", "ALREADY CHECKED IN", "#f59e0b", blockHtml), "text/html");
    }

    if (!checkinDatabase.ContainsKey(id))
    {
        Console.WriteLine("  => RESULT:     [REJECTED] (Invalid Session)");
        string errorHtml = "<p><strong>Result:</strong> Access Denied</p><p>This check-in code does not exist in the active seminar session registry.</p>";
        return Results.Content(RenderMobileView("Verification Rejected", "INVALID SESSION", "#ef4444", errorHtml), "text/html");
    }

    if (checkinDatabase[id] == "Checked-In")
    {
        Console.WriteLine("  => RESULT:     [BLOCKED] (Duplicate Session Entry)");
        string usedHtml = "<p><strong>Result:</strong> Duplicate Entry Blocked</p><p>This credential has already been scanned and redeemed by someone else.</p>";
        return Results.Content(RenderMobileView("Access Already Claimed", "DUPLICATE ENTRY", "#f59e0b", usedHtml), "text/html");
    }

    // Success Actions
    checkinDatabase[id] = "Checked-In";
    registeredDevices[fingerprint] = DateTime.UtcNow; // Lock this device out of future scans

    Console.WriteLine("  => RESULT:     [SUCCESS] (Access Granted)");
    string successHtml = $"<p><strong>Session ID:</strong> {id}</p><p><strong>Status:</strong> Present</p><p><strong>Timestamp:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>";
    return Results.Content(RenderMobileView("Check-In Complete", "ENTRY GRANTED", "#22c55e", successHtml), "text/html");
}).RequireRateLimiting("BasicLimiter");

// Polling endpoint
app.MapGet("/checkin/status/{id}", (string id) =>
{
    if (checkinDatabase.TryGetValue(id, out var status)) return Results.Ok(new { status });
    return Results.NotFound();
});


// ==========================================
// 2. DIGITAL TICKET API & VERIFICATION
// ==========================================

// JSON endpoint for dashboard
app.MapGet("/api/ticket/create", ([FromQuery] string baseUrl, [FromQuery] string attendee, [FromQuery] string tier, [FromQuery] bool forged = false, [FromQuery] int expiresIn = 86400) =>
{
    string ticketId = $"TKT-{Guid.NewGuid().ToString()[..8].ToUpper()}";

    // Convert duration to absolute Unix timestamp
    long expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeSeconds();

    // 1. Setup the Authentic Data
    var authenticData = new
    {
        TicketId = ticketId,
        Attendee = string.IsNullOrWhiteSpace(attendee) ? "Tran Quang Duy" : attendee,
        Tier = string.IsNullOrWhiteSpace(tier) ? "VIP-PREMIUM" : tier,
        Event = "SOA Architecture Seminar 2026",
        ExpiresAt = expiresAt
    };

    string jsonPayload = JsonSerializer.Serialize(authenticData);
    string base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonPayload));

    // 2. Generate the Authentic Signature
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ServerSecretKey));
    byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(base64Payload));
    string signature = Convert.ToBase64String(hash);

    // 3. Forgery Simulation Logic
    string returnedAttendee = authenticData.Attendee;
    if (forged)
    {
        var tamperedData = new
        {
            TicketId = ticketId,
            Attendee = "HACKER (Forged Entry)",
            Tier = "VIP-PREMIUM",
            Event = "SOA Architecture Seminar 2026",
            ExpiresAt = expiresAt
        };

        string tamperedJson = JsonSerializer.Serialize(tamperedData);
        base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(tamperedJson));
        returnedAttendee = tamperedData.Attendee;
    }

    // 4. Construct URL with Payload
    string verifyUrl = $"{baseUrl.TrimEnd('/')}/ticket/verify?payload={Uri.EscapeDataString(base64Payload)}&sig={Uri.EscapeDataString(signature)}";
    string qrBase64 = GenerateQrBase64(verifyUrl);

    return Results.Ok(new { ticketId, attendee = returnedAttendee, tier = authenticData.Tier, signature, qrBase64, verifyUrl });
});

// Mobile verification endpoint
app.MapGet("/ticket/verify", ([FromQuery] string payload, [FromQuery] string sig, HttpContext context) =>
{
    Console.WriteLine($"\n[AUDIT] Digital Ticket Scan Attempt at {DateTime.Now:T}");
    Console.WriteLine($"  ├─ Scanner:    {GetClientInfo(context)}");

    if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(sig))
    {
        Console.WriteLine("  => RESULT:     [REJECTED] (Missing Cryptographic Data)");
        string invalidHtml = "<p><strong>Result:</strong> Missing Payload</p><p>The scanned ticket does not contain the required cryptographic parameters.</p>";
        return Results.Content(RenderMobileView("Malformed Ticket", "ERROR", "#ef4444", invalidHtml), "text/html");
    }

    // Phase 1: Cryptographic Integrity Check
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ServerSecretKey));
    byte[] computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    string computedSignature = Convert.ToBase64String(computedHash);

    if (computedSignature != sig)
    {
        Console.WriteLine("  => RESULT:     [FORGERY DETECTED] (Signature Mismatch)");
        string forgeryHtml = "<p><strong>Security Event:</strong> Signature Mismatch</p><p>The cryptographic signature is invalid. The ticket payload has been modified or forged.</p>";
        return Results.Content(RenderMobileView("Security Violation", "FORGERY DETECTED", "#ef4444", forgeryHtml), "text/html");
    }

    string jsonPayload = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
    var data = JsonSerializer.Deserialize<JsonElement>(jsonPayload);

    // Phase 2: Expiration Check
    long expiresAt = data.GetProperty("ExpiresAt").GetInt64();
    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAt)
    {
        Console.WriteLine("  => RESULT:     [EXPIRED] (Ticket Time Limit Exceeded)");
        string expiredHtml = "<p><strong>Security Event:</strong> Ticket Expired</p><p>This ticket's validity period has passed. Access denied.</p>";
        return Results.Content(RenderMobileView("Ticket Expired", "EXPIRED", "#f59e0b", expiredHtml), "text/html");
    }

    // Success
    Console.WriteLine($"  => RESULT:     [AUTHENTIC] (Attendee: {data.GetProperty("Attendee").GetString()})");
    DateTimeOffset expiryDate = DateTimeOffset.FromUnixTimeSeconds(expiresAt);

    string verifiedHtml = $@"
        <p><strong>Event:</strong> {data.GetProperty("Event").GetString()}</p>
        <p><strong>Attendee:</strong> {data.GetProperty("Attendee").GetString()}</p>
        <p><strong>Tier:</strong> {data.GetProperty("Tier").GetString()}</p>
        <p><strong>Ticket ID:</strong> {data.GetProperty("TicketId").GetString()}</p>
        <p style='margin-top: 10px; font-size: 12px; color: #64748b;'>Valid Until: {expiryDate:yyyy-MM-dd HH:mm:ss} UTC<br>Cryptographic Signature Verified</p>";

    return Results.Content(RenderMobileView("Digital Ticket Verified", "AUTHENTIC TICKET", "#22c55e", verifiedHtml), "text/html");
}).RequireRateLimiting("BasicLimiter");


// ==========================================
// 3. PRODUCT QR API & CANONICAL VIEW
// ==========================================

// JSON endpoint for dashboard
app.MapGet("/api/product/create", ([FromQuery] string baseUrl, [FromQuery] string productId) =>
{
    string id = string.IsNullOrWhiteSpace(productId) ? "PROD-001" : productId;
    if (!productDatabase.ContainsKey(id)) id = "PROD-001";

    var product = productDatabase[id];
    string productUrl = $"{baseUrl.TrimEnd('/')}/product/view/{id}";
    string qrBase64 = GenerateQrBase64(productUrl);

    return Results.Ok(new { productId = id, productName = product.Name, qrBase64, productUrl });
});

// Mobile product details endpoint
app.MapGet("/product/view/{id}", (string id, HttpContext context) =>
{
    Console.WriteLine($"\n[AUDIT] Product Catalog Accessed at {DateTime.Now:T}");
    Console.WriteLine($"  ├─ Product ID: {id}");
    Console.WriteLine($"  ├─ Viewer:     {GetClientInfo(context)}");

    if (productDatabase.TryGetValue(id, out var product))
    {
        string detailsHtml = $@"
            <p><strong>Product Name:</strong> {product.Name}</p>
            <p><strong>SKU:</strong> {product.Sku}</p>
            <p><strong>Price:</strong> {product.Price}</p>
            <p><strong>Category:</strong> {product.Category}</p>
            <p style='margin-top: 10px; font-size: 12px; color: #64748b;'>Stateless Canonical Reference · Unlimited Multi-Use</p>";

        return Results.Content(RenderMobileView("Product Information", "OFFICIAL CATALOG", "#38bdf8", detailsHtml), "text/html");
    }

    string notFoundHtml = "<p>The requested product identifier does not exist in the centralized inventory service.</p>";
    return Results.Content(RenderMobileView("Item Not Found", "UNRESOLVED SKU", "#ef4444", notFoundHtml), "text/html");
}).RequireRateLimiting("BasicLimiter");

app.Run();
