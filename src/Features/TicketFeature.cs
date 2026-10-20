using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QRCodeApi.Common;

namespace QRCodeApi.Features;

public class TicketRepository
{
    private readonly ConcurrentDictionary<string, DateTime> _usedTickets = new();
    public bool IsTicketRedeemed(string ticketId) => _usedTickets.ContainsKey(ticketId);
    public void BurnTicket(string ticketId) => _usedTickets[ticketId] = DateTime.UtcNow;
}

public class TicketModule : IQrModule
{
    public void RegisterEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/ticket/create", CreateTicket).WithTags("2. Digital Ticket API").WithSummary("Issues a secure HMAC-SHA256 signed digital ticket");
        app.MapGet("/ticket/verify", VerifyTicket).RequireRateLimiting("BasicLimiter").WithTags("2. Digital Ticket API").WithSummary("Client endpoint for cryptographic payload validation");
    }

    private static IResult CreateTicket([FromQuery] string baseUrl, [FromQuery] string attendee, [FromQuery] string tier, [FromQuery] bool forged, [FromQuery] int expiresIn, IQrGeneratorService qr, IIdentitySecurityService security)
    {
        string ticketId = $"TKT-{Guid.NewGuid().ToString()[..8].ToUpper()}";
        long expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeSeconds();

        var authenticData = new { TicketId = ticketId, Attendee = string.IsNullOrWhiteSpace(attendee) ? "John Doe" : attendee, Tier = string.IsNullOrWhiteSpace(tier) ? "VIP-PREMIUM" : tier, Event = "SOA Architecture Seminar 2026", ExpiresAt = expiresAt };
        string base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(authenticData)));
        string signature = security.GenerateHmacSignature(base64Payload);
        string returnedAttendee = authenticData.Attendee;

        if (forged)
        {
            var tamperedData = new { TicketId = ticketId, Attendee = "HACKER (Forged Entry)", Tier = "VIP-PREMIUM", Event = "SOA Architecture Seminar 2026", ExpiresAt = expiresAt };
            base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tamperedData)));
            returnedAttendee = tamperedData.Attendee;
        }

        string verifyUrl = $"{baseUrl.TrimEnd('/')}/ticket/verify?payload={Uri.EscapeDataString(base64Payload)}&sig={Uri.EscapeDataString(signature)}";
        return Results.Ok(new { ticketId, attendee = returnedAttendee, signature, qrBase64 = qr.GenerateQrBase64(verifyUrl), verifyUrl });
    }

    private static IResult VerifyTicket([FromQuery] string payload, [FromQuery] string sig, HttpContext context, TicketRepository repo, IIdentitySecurityService security, IHtmlTemplateService html)
    {
        Console.WriteLine($"\n[AUDIT] Digital Ticket Scan Attempt at {DateTime.Now:T}\n  ├─ Scanner:    {security.GetClientInfo(context)}");

        if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(sig)) return Results.Content(html.RenderMobileView("Malformed Ticket", "ERROR", "#ef4444", "<p>Missing Cryptographic Data.</p>"), "text/html");

        if (!security.VerifyHmacSignature(payload, sig))
        {
            Console.WriteLine("  => RESULT:     [FORGERY DETECTED] (Signature Mismatch)");
            return Results.Content(html.RenderMobileView("Security Violation", "FORGERY DETECTED", "#ef4444", "<p>The cryptographic signature is invalid.</p>"), "text/html");
        }

        var data = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > data.GetProperty("ExpiresAt").GetInt64()) return Results.Content(html.RenderMobileView("Ticket Expired", "EXPIRED", "#f59e0b", "<p>This ticket has expired.</p>"), "text/html");

        string ticketId = data.GetProperty("TicketId").GetString() ?? "UNKNOWN";
        if (repo.IsTicketRedeemed(ticketId)) return Results.Content(html.RenderMobileView("Access Denied", "ALREADY REDEEMED", "#ef4444", "<p>This single-use ticket has already been scanned.</p>"), "text/html");

        repo.BurnTicket(ticketId);

        Console.WriteLine($"  => RESULT:     [AUTHENTIC] (Attendee: {data.GetProperty("Attendee").GetString()})");
        return Results.Content(html.RenderMobileView("Digital Ticket Verified", "AUTHENTIC TICKET", "#22c55e", $"<p><strong>Event:</strong> {data.GetProperty("Event").GetString()}</p><p><strong>Attendee:</strong> {data.GetProperty("Attendee").GetString()}</p><p><strong>Ticket ID:</strong> {ticketId}</p>"), "text/html");
    }
}
