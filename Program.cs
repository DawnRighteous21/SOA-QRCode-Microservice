using QRCodeApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 1. Load Services
builder.Services.AddEnterpriseInfrastructure();

var app = builder.Build();

// 2. Configure HTTP Pipeline
app.UseSwagger();
app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "SOA QR API v1"); });
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

// 3. Auto-Wire Endpoints
app.MapQrModules();

app.Run();
