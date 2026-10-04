using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Steward.Api.Endpoints;
using SwiftBets.Steward.Application;
using SwiftBets.Steward.Infrastructure;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-steward");
builder.Services.AddSwiftBetsWeb();
builder.Services.AddSwiftBetsJwtBearer(builder.Configuration);
builder.Services.AddAuthorizationBuilder().AddPolicy(StatusEndpoints.Permission, p => p.RequireAssertion(c =>
    c.User.IsInRole(Roles.Admin) || c.User.HasClaim("perm", StatusEndpoints.Permission)));
builder.Services.AddStewardApplication();
builder.Services.AddStewardInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.UseAuthentication();
app.UseAuthorization();
app.MapSwiftBetsOperationalEndpoints();
app.MapStewardEndpoints();
app.MapAlertWebhook();
app.MapStatusEndpoints();

await app.RunAsync();
return 0;

public partial class Program;
