using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using ProjectTango.Application;
using ProjectTango.Application.Common;
using ProjectTango.Infrastructure;
using ProjectTango.Infrastructure.Persistence;
using ProjectTango.Web.Auth;
using ProjectTango.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// `dotnet run -- migrate` applies pending DbUp scripts and exits (used by CI/deploy).
// In Development, Database:MigrateOnStartup=true also applies them on normal startup.
if (args.Contains("migrate"))
{
    DatabaseMigrator.MigrateToLatest(GetConnectionString(builder.Configuration));
    return;
}

const string ApiCorsPolicy = "ApiCors";

// Razor UI signs in with OIDC + cookies; /api/v1 accepts Entra-issued JWT bearer
// tokens (the path future mobile/desktop clients use). Same app registration.
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
builder.Services.AddAuthentication()
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"), JwtBearerDefaults.AuthenticationScheme);

// First sign-in provisioning (design-doc.md §4.2): resolve the Entra identity to an
// employee record (linking by email if one pre-exists) and stamp the identity with the
// employee id + role claims. Role grants take effect at the next sign-in. The enrichment is
// shared with the JWT bearer path below so API/mobile requests resolve the same identity.
builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    // Authorization code flow + PKCE (design-doc §4.1) — not the library default
    // (implicit id_token), which Entra rejects unless enabled per-registration.
    options.ResponseType = OpenIdConnectResponseType.Code;

    var previousOnTokenValidated = options.Events.OnTokenValidated;
    options.Events.OnTokenValidated = async context =>
    {
        await previousOnTokenValidated(context);
        await EmployeeClaimsEnricher.EnrichAsync(context.Principal!, context.HttpContext.RequestServices);
    };
});

// API JWT bearer path: mirror the cookie enrichment so a bearer token resolves to the same
// employee id + role claims. Without this, ICurrentUser.EmployeeId would be null for mobile
// clients. PostConfigure runs after Microsoft.Identity.Web wires its own events, so we chain
// (never replace) the existing OnTokenValidated handler.
builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events ??= new JwtBearerEvents();
    var previousOnTokenValidated = options.Events.OnTokenValidated;
    options.Events.OnTokenValidated = async context =>
    {
        if (previousOnTokenValidated is not null)
        {
            await previousOnTokenValidated(context);
        }

        await EmployeeClaimsEnricher.EnrichAsync(context.Principal!, context.HttpContext.RequestServices);
    };
});

builder.Services.AddAuthorization();

// Cross-origin access for API clients (browser SPAs, mobile web). Origins are configured per
// environment (empty by default → no cross-origin access); the policy is applied to /api routes.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy(ApiCorsPolicy, policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    }));

// RFC 7807 problem+json for API error responses (design-doc §7).
builder.Services.AddProblemDetails();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

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

app.UseCors(ApiCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// Published in all environments: the OpenAPI document is the contract for API/mobile clients
// (design-doc §7). Served anonymously at /openapi/v1.json.
app.MapOpenApi();

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
