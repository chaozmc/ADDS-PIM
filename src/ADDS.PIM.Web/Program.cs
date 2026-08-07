using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Web.Components;
using ADDS.PIM.Web.Prototype;
using ADDS.PIM.Web.Operator;
using ADDS.PIM.Web.Administration;
using ADDS.PIM.Web.Security;
using ADDS.PIM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.IISIntegration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
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
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization(PimUserAccessPolicy.Name);

app.Run();
