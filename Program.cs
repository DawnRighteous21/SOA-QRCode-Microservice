using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using QRCoder;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// API INTEGRATION & DOCUMENTATION (SWAGGER)
// ==========================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(setup =>
{
    setup.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SOA QR Architecture API",
        Version = "v1",
        Description = "Enterprise Stateless/Stateful QR Code Microservice for TDTU Seminar"
    });
});

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

// Activate Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SOA QR API v1");
});

app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

// ==========================================
// IN-MEMORY DATABASES & CONFIG
// ==========================================

// Event Databases
var activeEvents = new ConcurrentDictionary<string, EventData>();
var deviceAttendance = new ConcurrentDictionary<string, DateTime>();

// Stateful Ticket Database
var usedTickets = new ConcurrentDictionary<string, DateTime>();

// Product Database
var productDatabase = new ConcurrentDictionary<string, (string Name, string Sku, string Price, string Category)>();
productDatabase["PROD-001"] = ("SteelSeries Arctis 5 Headset", "SKU-AUDIO-001", "$99.99", "Peripherals");
productDatabase["PROD-002"] = ("Keychron Q1 Pro Mechanical Keyboard", "SKU-KB-002", "$199.00", "Custom Keyboards");

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

string GetClientInfo(HttpContext context)
{
    var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString() ?? "Unknown IP";
    var agent = context.Request.Headers.UserAgent.ToString();
    return $"IP: {ip}\n  └─ Device:     {agent}";
}

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

app.MapGet("/api/checkin/create", ([FromQuery] string baseUrl, [FromQuery] int duration) =>
{
    string eventId = $"EVT-{Guid.NewGuid().ToString()[..6].ToUpper()}";
    long expiresAt = duration > 0 ? DateTimeOffset.UtcNow.AddSeconds(duration).ToUnixTimeSeconds() : long.MaxValue;

    activeEvents[eventId] = new EventData(0, expiresAt);

    string verifyUrl = $"{baseUrl.TrimEnd('/')}/checkin/verify/{eventId}";
    string qrBase64 = GenerateQrBase64(verifyUrl);

    return Results.Ok(new { eventId, qrBase64, verifyUrl });
})
.WithTags("1. Event Check-In API")
.WithSummary("Generates a stateful broadcast Event Check-In QR code");

app.MapPost("/api/checkin/reset", () =>
{
    activeEvents.Clear();
    deviceAttendance.Clear();
    usedTickets.Clear();
    Console.WriteLine($"\n[ADMIN] {DateTime.Now:T} - Active Event Queue and All Trackers Cleared.");
    return Results.Ok();
})
.WithTags("1. Event Check-In API")
.WithSummary("Clears all active sessions and device fingerprints (Admin)");

app.MapGet("/api/checkin/attendees/{id}", (string id) =>
{
    var attendees = deviceAttendance
        .Where(kvp => kvp.Key.StartsWith(id + "_"))
        .Select(kvp =>
        {
            var parts = kvp.Key.Substring(id.Length + 1).Split("::");
            return new
            {
                IpAddress = parts[0],
                Device = parts.Length > 1 ? parts[1] : "Unknown Device",
                Timestamp = kvp.Value.ToString("HH:mm:ss")
            };
        })
        .OrderByDescending(a => a.Timestamp)
        .ToList();

    return Results.Ok(attendees);
})
.WithTags("1. Event Check-In API")
.WithSummary("Fetches the live list of authenticated attendee fingerprints");

app.MapGet("/checkin/verify/{id}", (string id, HttpContext context) =>
{
    Console.WriteLine($"\n[AUDIT] Broadcast Scan Attempt at {DateTime.Now:T}");
    Console.WriteLine($"  ├─ Event ID:   {id}");
    Console.WriteLine($"  ├─ Scanner:    {GetClientInfo(context)}");

    if (!activeEvents.TryGetValue(id, out var eventData))
    {
        Console.WriteLine("  => RESULT:     [REJECTED] (Invalid Event Session)");
        string errorHtml = "<p><strong>Result:</strong> Access Denied</p><p>This check-in session is not currently active.</p>";
        return Results.Content(RenderMobileView("Verification Rejected", "INVALID SESSION", "#ef4444", errorHtml), "text/html");
    }

    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > eventData.ExpiresAt)
    {
        Console.WriteLine("  => RESULT:     [EXPIRED] (Session Window Closed)");
        string expiredHtml = "<p><strong>Result:</strong> Session Closed</p><p>This check-in window has expired. Please ask the instructor to reopen the session.</p>";
        return Results.Content(RenderMobileView("Registration Closed", "EXPIRED", "#ef4444", expiredHtml), "text/html");
    }

    string fingerprint = GetDeviceFingerprint(context);
    string attendanceKey = $"{id}_{fingerprint}";

    if (deviceAttendance.ContainsKey(attendanceKey))
    {
        Console.WriteLine("  => RESULT:     [BLOCKED] (Device Already Registered in Event)");
        string blockHtml = "<p><strong>Result:</strong> Attendance Recorded</p><p>You have already checked into this event using this device. Thank you!</p>";
        return Results.Content(RenderMobileView("Device Recognized", "ALREADY CHECKED IN", "#f59e0b", blockHtml), "text/html");
    }

    deviceAttendance[attendanceKey] = DateTime.UtcNow;
    activeEvents.AddOrUpdate(id, new EventData(1, eventData.ExpiresAt), (_, oldData) => new EventData(oldData.Count + 1, oldData.ExpiresAt));

    Console.WriteLine($"  => RESULT:     [SUCCESS] (Attendee Verified. Total: {activeEvents[id].Count})");
    string successHtml = $"<p><strong>Event ID:</strong> {id}</p><p><strong>Status:</strong> Present</p><p><strong>Timestamp:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>";
    return Results.Content(RenderMobileView("Check-In Complete", "ENTRY GRANTED", "#22c55e", successHtml), "text/html");
})
.RequireRateLimiting("BasicLimiter")
.WithTags("1. Event Check-In API")
.WithSummary("Client endpoint for mobile QR scanning and validation");

app.MapGet("/checkin/status/{id}", (string id) =>
{
    if (activeEvents.TryGetValue(id, out var eventData))
    {
        bool isExpired = DateTimeOffset.UtcNow.ToUnixTimeSeconds() > eventData.ExpiresAt;
        return Results.Ok(new { count = eventData.Count, isExpired });
    }
    return Results.NotFound();
})
.WithTags("1. Event Check-In API")
.WithSummary("Polling endpoint to retrieve the live attendee count");


// ==========================================
// 2. DIGITAL TICKET API & VERIFICATION
// ==========================================

app.MapGet("/api/ticket/create", ([FromQuery] string baseUrl, [FromQuery] string attendee, [FromQuery] string tier, [FromQuery] bool forged = false, [FromQuery] int expiresIn = 86400) =>
{
    string ticketId = $"TKT-{Guid.NewGuid().ToString()[..8].ToUpper()}";
    long expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeSeconds();

    var authenticData = new
    {
        TicketId = ticketId,
        Attendee = string.IsNullOrWhiteSpace(attendee) ? "John Doe" : attendee,
        Tier = string.IsNullOrWhiteSpace(tier) ? "VIP-PREMIUM" : tier,
        Event = "SOA Architecture Seminar 2026",
        ExpiresAt = expiresAt
    };

    string jsonPayload = JsonSerializer.Serialize(authenticData);
    string base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonPayload));

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ServerSecretKey));
    byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(base64Payload));
    string signature = Convert.ToBase64String(hash);

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

    string verifyUrl = $"{baseUrl.TrimEnd('/')}/ticket/verify?payload={Uri.EscapeDataString(base64Payload)}&sig={Uri.EscapeDataString(signature)}";
    string qrBase64 = GenerateQrBase64(verifyUrl);

    return Results.Ok(new { ticketId, attendee = returnedAttendee, signature, qrBase64, verifyUrl });
})
.WithTags("2. Digital Ticket API")
.WithSummary("Issues a secure HMAC-SHA256 signed digital ticket");

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

    long expiresAt = data.GetProperty("ExpiresAt").GetInt64();
    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAt)
    {
        Console.WriteLine("  => RESULT:     [EXPIRED] (Ticket Time Limit Exceeded)");
        string expiredHtml = "<p><strong>Security Event:</strong> Ticket Expired</p><p>This ticket's validity period has passed. Access denied.</p>";
        return Results.Content(RenderMobileView("Ticket Expired", "EXPIRED", "#f59e0b", expiredHtml), "text/html");
    }

    // STATEFUL VALIDATION (Single-Use Enforcement)
    string ticketId = data.GetProperty("TicketId").GetString() ?? "UNKNOWN";
    Console.WriteLine($"  ├─ Mode:       Stateful (Checking Database)");

    if (usedTickets.ContainsKey(ticketId))
    {
        Console.WriteLine("  => RESULT:     [BLOCKED] (Ticket Already Redeemed)");
        string usedHtml = "<p><strong>Security Event:</strong> Ticket Already Redeemed</p><p>This single-use ticket has already been scanned and cannot be reused.</p>";
        return Results.Content(RenderMobileView("Access Denied", "ALREADY REDEEMED", "#ef4444", usedHtml), "text/html");
    }

    // Burn the ticket
    usedTickets[ticketId] = DateTime.UtcNow;

    Console.WriteLine($"  => RESULT:     [AUTHENTIC] (Attendee: {data.GetProperty("Attendee").GetString()})");
    DateTimeOffset expiryDate = DateTimeOffset.FromUnixTimeSeconds(expiresAt);

    string verifiedHtml = $@"
        <p><strong>Event:</strong> {data.GetProperty("Event").GetString()}</p>
        <p><strong>Attendee:</strong> {data.GetProperty("Attendee").GetString()}</p>
        <p><strong>Ticket ID:</strong> {ticketId}</p>
        <p style='margin-top: 10px; font-size: 12px; color: #64748b;'>Valid Until: {expiryDate:yyyy-MM-dd HH:mm:ss} UTC<br>Cryptographic Signature Verified</p>";

    return Results.Content(RenderMobileView("Digital Ticket Verified", "AUTHENTIC TICKET", "#22c55e", verifiedHtml), "text/html");
})
.RequireRateLimiting("BasicLimiter")
.WithTags("2. Digital Ticket API")
.WithSummary("Client endpoint for cryptographic payload validation");


// ==========================================
// 3. PRODUCT QR API & CANONICAL VIEW
// ==========================================

app.MapGet("/api/product/create", ([FromQuery] string baseUrl, [FromQuery] string productId) =>
{
    string id = string.IsNullOrWhiteSpace(productId) ? "PROD-001" : productId;
    if (!productDatabase.ContainsKey(id)) id = "PROD-001";

    var product = productDatabase[id];
    string productUrl = $"{baseUrl.TrimEnd('/')}/product/view/{id}";
    string qrBase64 = GenerateQrBase64(productUrl);

    return Results.Ok(new { productId = id, productName = product.Name, qrBase64, productUrl });
})
.WithTags("3. Product Catalog API")
.WithSummary("Generates a stateless multi-use product tag");

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
})
.RequireRateLimiting("BasicLimiter")
.WithTags("3. Product Catalog API")
.WithSummary("Client endpoint for routing to physical asset data");

app.Run();

// ==========================================
// TYPE DECLARATIONS
// ==========================================
public record EventData(int Count, long ExpiresAt);
