using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftBets.Steward.Application.Ports;
using SwiftBets.Steward.Domain.Status;

namespace SwiftBets.Steward.Infrastructure.Status;

/// <summary>Checks every component's readiness URLs on a timer and publishes the states to the board.</summary>
public sealed partial class StatusPoller(IHttpClientFactory clients, ComponentStatusBoard board, IOptions<StatusOptions> options, TimeProvider time, ILogger<StatusPoller> logger) : BackgroundService
{
    public const string ClientName = "status";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Components.Count == 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollSeconds), time);
        do
        {
            await PollOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var http = clients.CreateClient(ClientName);
        var results = await Task.WhenAll(options.Value.Components.Select(async c =>
        {
            var checks = await Task.WhenAll(c.Urls.Select(url => HealthyAsync(http, url, cancellationToken)));
            return new ComponentHealth(c.Key, c.Name, StatusPage.ComponentState(checks));
        }));
        board.Publish(results, time.GetUtcNow());
    }

    private async Task<bool> HealthyAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(url), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnhealthy(url, ex.Message);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Status check {Url} failed: {Reason}")]
    private partial void LogUnhealthy(string url, string reason);
}
