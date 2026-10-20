using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using QRCoder;

namespace QRCodeApi.Common;

public class QrGeneratorService : IQrGeneratorService
{
    public string GenerateQrBase64(string content)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrData);
        return Convert.ToBase64String(qrCode.GetGraphic(10));
    }
}

public class IdentitySecurityService : IIdentitySecurityService
{
    private const string ServerSecretKey = "TDTU_SOA_Cryptographic_Signature_Key_2026!";

    public string GetClientInfo(HttpContext context)
    {
        var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString() ?? "Unknown IP";
        var agent = context.Request.Headers.UserAgent.ToString();
        return $"IP: {ip}\n  └─ Device:     {agent}";
    }

    public string GetDeviceFingerprint(HttpContext context)
    {
        var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString() ?? "Unknown IP";
        var agent = context.Request.Headers.UserAgent.ToString();
        return $"{ip}::{agent}";
    }

    public string GenerateHmacSignature(string base64Payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ServerSecretKey));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(base64Payload));
        return Convert.ToBase64String(hash);
    }

    public bool VerifyHmacSignature(string base64Payload, string providedSignature)
    {
        return GenerateHmacSignature(base64Payload) == providedSignature;
    }
}

public class HtmlTemplateService : IHtmlTemplateService
{
    public string RenderMobileView(string title, string statusBadgeText, string badgeColor, string detailsHtml)
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
}
