using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using QRCodeApi.Common;

namespace QRCodeApi.Features;

public record EventData(int Count, long ExpiresAt);

public class CheckInRepository
{
    private readonly ConcurrentDictionary<string, EventData> _activeEvents = new();
    private readonly ConcurrentDictionary<string, DateTime> _deviceAttendance = new();

    public void CreateEvent(string eventId, long expiresAt) => _activeEvents[eventId] = new EventData(0, expiresAt);
    public void ResetAll() { _activeEvents.Clear(); _deviceAttendance.Clear(); }
    public bool TryGetEvent(string id, out EventData eventData) => _activeEvents.TryGetValue(id, out eventData!);

    public bool RecordAttendance(string eventId, string fingerprint, long expiresAt)
    {
        string attendanceKey = $"{eventId}_{fingerprint}";
        if (_deviceAttendance.ContainsKey(attendanceKey)) return false;

        _deviceAttendance[attendanceKey] = DateTime.UtcNow;
        _activeEvents.AddOrUpdate(eventId, new EventData(1, expiresAt), (_, oldData) => new EventData(oldData.Count + 1, oldData.ExpiresAt));
        return true;
    }

    public IEnumerable<object> GetLiveAttendees(string eventId) =>
        _deviceAttendance.Where(kvp => kvp.Key.StartsWith(eventId + "_"))
            .Select(kvp => {
                var parts = kvp.Key.Substring(eventId.Length + 1).Split("::");
                return new { IpAddress = parts[0], Device = parts.Length > 1 ? parts[1] : "Unknown Device", Timestamp = kvp.Value.ToString("HH:mm:ss") };
            }).OrderByDescending(a => a.Timestamp);
}

public class CheckInModule : IQrModule
{
    public void RegisterEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/checkin/create", CreateCheckIn).WithTags("1. Event Check-In API").WithSummary("Generates a stateful broadcast Event Check-In QR code");
        app.MapPost("/api/checkin/reset", ResetCheckIn).WithTags("1. Event Check-In API").WithSummary("Clears all active sessions and device fingerprints (Admin)");
        app.MapGet("/api/checkin/attendees/{id}", GetAttendees).WithTags("1. Event Check-In API").WithSummary("Fetches the live list of authenticated attendee fingerprints");
        app.MapGet("/checkin/verify/{id}", VerifyCheckIn).RequireRateLimiting("BasicLimiter").WithTags("1. Event Check-In API").WithSummary("Client endpoint for mobile QR scanning and validation");
        app.MapGet("/checkin/status/{id}", GetStatus).WithTags("1. Event Check-In API").WithSummary("Polling endpoint to retrieve the live attendee count");
    }

    private static IResult CreateCheckIn([FromQuery] string baseUrl, [FromQuery] int duration, IQrGeneratorService qr, CheckInRepository repo)
    {
        string eventId = $"EVT-{Guid.NewGuid().ToString()[..6].ToUpper()}";
        long expiresAt = duration > 0 ? DateTimeOffset.UtcNow.AddSeconds(duration).ToUnixTimeSeconds() : long.MaxValue;
        repo.CreateEvent(eventId, expiresAt);
        string verifyUrl = $"{baseUrl.TrimEnd('/')}/checkin/verify/{eventId}";
        return Results.Ok(new { eventId, qrBase64 = qr.GenerateQrBase64(verifyUrl), verifyUrl });
    }

    private static IResult ResetCheckIn(CheckInRepository repo)
    {
        repo.ResetAll();
        Console.WriteLine($"\n[ADMIN] {DateTime.Now:T} - Active Event Queue and All Trackers Cleared.");
        return Results.Ok();
    }

    private static IResult GetAttendees(string id, CheckInRepository repo) => Results.Ok(repo.GetLiveAttendees(id));

    private static IResult VerifyCheckIn(string id, HttpContext context, CheckInRepository repo, IIdentitySecurityService security, IHtmlTemplateService html)
    {
        Console.WriteLine($"\n[AUDIT] Broadcast Scan Attempt at {DateTime.Now:T}\n  ├─ Event ID:   {id}\n  ├─ Scanner:    {security.GetClientInfo(context)}");

        if (!repo.TryGetEvent(id, out var eventData)) return Results.Content(html.RenderMobileView("Verification Rejected", "INVALID SESSION", "#ef4444", "<p><strong>Result:</strong> Access Denied</p><p>This check-in session is not currently active.</p>"), "text/html");

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > eventData.ExpiresAt) return Results.Content(html.RenderMobileView("Registration Closed", "EXPIRED", "#ef4444", "<p><strong>Result:</strong> Session Closed</p><p>This check-in window has expired.</p>"), "text/html");

        if (!repo.RecordAttendance(id, security.GetDeviceFingerprint(context), eventData.ExpiresAt)) return Results.Content(html.RenderMobileView("Device Recognized", "ALREADY CHECKED IN", "#f59e0b", "<p><strong>Result:</strong> Attendance Recorded</p><p>You have already checked into this event using this device.</p>"), "text/html");

        repo.TryGetEvent(id, out var updatedData);
        Console.WriteLine($"  => RESULT:     [SUCCESS] (Attendee Verified. Total: {updatedData.Count})");
        return Results.Content(html.RenderMobileView("Check-In Complete", "ENTRY GRANTED", "#22c55e", $"<p><strong>Event ID:</strong> {id}</p><p><strong>Status:</strong> Present</p><p><strong>Timestamp:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>"), "text/html");
    }

    private static IResult GetStatus(string id, CheckInRepository repo)
    {
        if (repo.TryGetEvent(id, out var eventData)) return Results.Ok(new { count = eventData.Count, isExpired = DateTimeOffset.UtcNow.ToUnixTimeSeconds() > eventData.ExpiresAt });
        return Results.NotFound();
    }
}
