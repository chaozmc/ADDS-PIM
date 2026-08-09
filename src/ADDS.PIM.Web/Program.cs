using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Web.Components;
using ADDS.PIM.Web.Prototype;
using ADDS.PIM.Web.Operator;
using ADDS.PIM.Web.Administration;
using ADDS.PIM.Web.Security;
using ADDS.PIM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Server.IISIntegration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    string[] supportedCultures = ["de", "en"];
    options.SetDefaultCulture("de");
    options.AddSupportedCultures(supportedCultures);
    options.AddSupportedUICultures(supportedCultures);
    // Cookie-only: no Accept-Language auto-detection, so existing German-speaking
    // users are never silently switched to English by browser settings.
    options.RequestCultureProviders = [new CookieRequestCultureProvider()];
});
builder.Services.AddSingleton<IPrototypeGroupCatalog, PrototypeGroupCatalog>();
builder.Services.AddSingleton(TimeProvider.System);
var directoryScopeConfiguration = new DirectoryScopeConfiguration(
    Guid.TryParse(builder.Configuration["Directory:ScopeId"], out var directoryScopeId) ? directoryScopeId : Guid.Empty,
    builder.Configuration["Directory:DomainDnsName"] ?? string.Empty,
    builder.Configuration["Directory:ForestDnsName"] ?? string.Empty);
directoryScopeConfiguration.Validate();
builder.Services.AddSingleton(directoryScopeConfiguration);
var operatorOptions = builder.Configuration.GetSection(OperatorTestOptions.SectionName).Get<OperatorTestOptions>() ?? throw new InvalidOperationException("OperatorTest configuration is required.");
operatorOptions.Validate();
builder.Services.AddSingleton(operatorOptions);
builder.Services.AddScoped<ICurrentPimActorContext, CurrentPimActorContext>();
builder.Services.AddHttpClient<IOperatorRequestClient, OperatorRequestClient>(client => client.BaseAddress = new Uri(builder.Configuration["Api:BaseAddress"] ?? throw new InvalidOperationException("Api:BaseAddress is required.")));
builder.Services.AddHttpClient<IAdministrationClient, AdministrationClient>(client => client.BaseAddress = new Uri(builder.Configuration["Api:BaseAddress"] ?? throw new InvalidOperationException("Api:BaseAddress is required.")));
builder.Services.AddPimApplicationAccess(builder.Configuration);
builder.Services.AddAuthentication(IISDefaults.AuthenticationScheme);
builder.Services.AddAuthorizationBuilder().AddPolicy(PimUserAccessPolicy.Name, policy => policy
    .RequireAuthenticatedUser()
    .AddRequirements(new PimUserAccessRequirement()));
builder.Services.AddScoped<IAuthorizationHandler, PimUserAccessAuthorizationHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IPrototypeMembershipRequestClient, PrototypeMembershipRequestClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["PrototypeApi:BaseAddress"]
        ?? "http://localhost:5195/");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.UseRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization(PimUserAccessPolicy.Name);

// Not behind PimUserAccessPolicy: language must be selectable even from unauthenticated/error pages.
// Blazor Server negotiates culture once per circuit at connection time, so switching language requires
// a real HTTP round trip (this endpoint) rather than an in-circuit event handler, to force a reload.
app.MapGet("/culture/set", (HttpContext http, string culture, string redirectUri) =>
{
    var supportedCultures = new[] { "de", "en" };
    if (!supportedCultures.Contains(culture, StringComparer.OrdinalIgnoreCase))
    {
        return Results.BadRequest();
    }

    if (string.IsNullOrEmpty(redirectUri) || !Uri.IsWellFormedUriString(redirectUri, UriKind.Relative))
    {
        redirectUri = "/";
    }

    http.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax });

    return Results.LocalRedirect(redirectUri);
});

app.Run();
