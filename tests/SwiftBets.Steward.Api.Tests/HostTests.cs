using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SwiftBets.Steward.Api.Tests;

public sealed class HostTests : IClassFixture<HostTests.Factory>
{
    private readonly HttpClient _client;

    public HostTests(Factory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Liveness_is_healthy_without_dependencies()
    {
        using var response = await _client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_reports_unavailable_dependencies()
    {
        using var response = await _client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Unknown_route_returns_the_error_envelope()
    {
        using var response = await _client.GetAsync(new Uri("/no-such-route", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Metrics_are_exposed()
    {
        var body = await _client.GetStringAsync(new Uri("/metrics", UriKind.Relative), TestContext.Current.CancellationToken);

        body.ShouldContain("process_cpu_seconds_total");
    }

    [Fact]
    public async Task Alertmanager_without_the_shared_token_is_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/alerts/alertmanager", UriKind.Relative))
        {
            Content = new StringContent("""{"status":"firing","alerts":[{"status":"firing","labels":{"alertname":"WalletLedgerDrift"}}]}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "wrong");

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Status_incidents_refuse_staff_without_the_status_permission()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/status/incidents", UriKind.Relative));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Factory.Token("reports.read"));

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Status_incidents_pass_authorisation_with_the_status_permission()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/status/incidents", UriKind.Relative));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Factory.Token("status.write"));

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        // No database in this host, so the store fails after authorisation let the call through.
        response.StatusCode.ShouldNotBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private const string Issuer = "https://identity.test";
        private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "steward.test" };

        public static string Token(params string[] permissions)
        {
            var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) };
            claims.AddRange(permissions.Select(p => new Claim("perm", p)));
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = "swiftbets",
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(10),
                SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
            });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
        builder.UseSetting("ConnectionStrings:SbSteward", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1");
        builder.UseSetting("Kafka:BootstrapServers", "127.0.0.1:1");
            builder.UseSetting("Jwt:Authority", "https://identity.test");
            builder.UseSetting("Steward:RunDetectors", "false");
            builder.UseSetting("Steward:AlertWebhookToken", "alert-token");
            builder.UseSetting("Steward:ModelProvider:Provider", "disabled");
            builder.UseSetting("ServiceIdentity:TokenEndpoint", "http://127.0.0.1:1/auth/token");
            builder.UseSetting("ServiceIdentity:ClientSecret", "unused");
            builder.UseSetting("Platform:SettlementAddress", "http://127.0.0.1:1");
            builder.UseSetting("Platform:PayoutAddress", "http://127.0.0.1:1");
            builder.UseSetting("Platform:OfferAddress", "http://127.0.0.1:1");
            builder.UseSetting("Platform:WalletAddress", "http://127.0.0.1:1");
            builder.UseSetting("Platform:PrometheusAddress", "http://127.0.0.1:1");
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(Key);
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.ValidAudience = "swiftbets";
            }));
        }
    }
}
