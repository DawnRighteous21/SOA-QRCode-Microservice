using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;

namespace QRCodeApi.Common;

public interface IQrModule
{
    void RegisterEndpoints(IEndpointRouteBuilder app);
}

public interface IQrGeneratorService
{
    string GenerateQrBase64(string content);
}

public interface IIdentitySecurityService
{
    string GetClientInfo(HttpContext context);
    string GetDeviceFingerprint(HttpContext context);
    string GenerateHmacSignature(string base64Payload);
    bool VerifyHmacSignature(string base64Payload, string providedSignature);
}

public interface IHtmlTemplateService
{
    string RenderMobileView(string title, string statusBadgeText, string badgeColor, string detailsHtml);
}
