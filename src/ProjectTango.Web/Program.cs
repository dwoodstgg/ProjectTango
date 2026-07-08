using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using ProjectTango.Application;
using ProjectTango.Infrastructure;
using ProjectTango.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// `dotnet run -- migrate` applies pending DbUp scripts and exits (used by CI/deploy).
// In Development, Database:MigrateOnStartup=true also applies them on normal startup.
if (args.Contains("migrate"))
{
    DatabaseMigrator.MigrateToLatest(GetConnectionString(builder.Configuration));
    return;
}

// Razor UI signs in with OIDC + cookies; /api/v1 accepts Entra-issued JWT bearer
// tokens (the path future mobile/desktop clients use). Same app registration.
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
builder.Services.AddAuthentication()
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"), JwtBearerDefaults.AuthenticationScheme);

builder.Services.AddAuthorization();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    DatabaseMigrator.MigrateToLatest(GetConnectionString(app.Configuration));
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // serves /openapi/v1.json
}

app.MapHealthChecks("/health").AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

static string GetConnectionString(IConfiguration configuration) =>
    configuration.GetConnectionString("ProjectTango")
    ?? throw new InvalidOperationException("Connection string 'ProjectTango' is missing.");

public partial class Program;
