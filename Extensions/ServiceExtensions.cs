using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using QRCodeApi.Common;
using QRCodeApi.Features;

namespace QRCodeApi.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddEnterpriseInfrastructure(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(setup => { setup.SwaggerDoc("v1", new OpenApiInfo { Title = "SOA QR Architecture API", Version = "v1" }); });

        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("BasicLimiter", opt => { opt.Window = TimeSpan.FromSeconds(1); opt.PermitLimit = 10; });
            options.OnRejected = async (context, token) =>
            {
                Console.WriteLine($"\n[SECURITY ALERT] {DateTime.Now:T} - DDoS Protection triggered! Dropping requests.");
                context.HttpContext.Response.StatusCode = 429;
                await context.HttpContext.Response.WriteAsync("DDoS Protection: Too many requests.", token);
            };
        });

        // Register Core Services
        services.AddSingleton<IQrGeneratorService, QrGeneratorService>();
        services.AddSingleton<IIdentitySecurityService, IdentitySecurityService>();
        services.AddSingleton<IHtmlTemplateService, HtmlTemplateService>();

        // Register Repositories
        services.AddSingleton<CheckInRepository>();
        services.AddSingleton<TicketRepository>();
        services.AddSingleton<ProductRepository>();

        // Register feature modules via the interface
        services.AddTransient<IQrModule, CheckInModule>();
        services.AddTransient<IQrModule, TicketModule>();
        services.AddTransient<IQrModule, ProductModule>();

        return services;
    }

    public static WebApplication MapQrModules(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        foreach (var module in scope.ServiceProvider.GetServices<IQrModule>())
        {
            module.RegisterEndpoints(app);
        }
        return app;
    }
}
