using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyInvoisGateway.Api.Data;
using MyInvoisGateway.Api.Lhdn;
using MyInvoisGateway.Api.Services;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=gateway.db"));

builder.Services.Configure<LhdnOptions>(builder.Configuration.GetSection(LhdnOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHttpClient("lhdn-token", (sp, c) =>
        c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<LhdnOptions>>().Value.BaseUrl))
    .AddStandardResilienceHandler();
builder.Services.AddSingleton<ITokenService>(sp => new TokenService(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("lhdn-token"),
    sp.GetRequiredService<IOptions<LhdnOptions>>(),
    sp.GetRequiredService<TimeProvider>()));

builder.Services.AddHttpClient("lhdn", (sp, c) =>
        c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<LhdnOptions>>().Value.BaseUrl))
    .AddStandardResilienceHandler();
builder.Services.AddScoped<IMyInvoisClient>(sp => new MyInvoisHttpClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("lhdn"),
    sp.GetRequiredService<ITokenService>()));

builder.Services.AddScoped<IInvoiceService, InvoiceService>();

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("myinvois-gateway"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddConsoleExporter());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

namespace MyInvoisGateway.Api
{
    public class ApiMarker;
}
