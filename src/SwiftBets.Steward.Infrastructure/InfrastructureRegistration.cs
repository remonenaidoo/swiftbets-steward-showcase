using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Resilience;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Casino;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Offer;
using SwiftBets.Contracts.Payments;
using SwiftBets.Contracts.Payout;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Steward.Application.Agent;
using SwiftBets.Steward.Application.Ports;
using SwiftBets.Steward.Infrastructure.Messaging;
using SwiftBets.Steward.Infrastructure.Model;
using SwiftBets.Steward.Infrastructure.Persistence;
using SwiftBets.Steward.Infrastructure.Platform;
using SwiftBets.Steward.Infrastructure.Runbooks;
using SwiftBets.Steward.Infrastructure.Status;
using SwiftBets.Steward.Infrastructure.Workers;

namespace SwiftBets.Steward.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddStewardInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPostgresPersistence(Required(configuration, "ConnectionStrings:SbSteward"));
        services.AddKafkaMessaging(configuration);
        services.AddFaultInjection(configuration);
        services.AddValidatedOptions<StewardOptions>(configuration, StewardOptions.SectionName);
        services.AddValidatedOptions<PlatformOptions>(configuration, PlatformOptions.SectionName);

        services.AddSingleton<PostgresEventLog>();
        services.AddSingleton<IEventLog>(sp => sp.GetRequiredService<PostgresEventLog>());
        services.AddSingleton<PostgresIncidentStore>();
        services.AddSingleton<IIncidentStore>(sp => new NotifyingIncidentStore(
            sp.GetRequiredService<PostgresIncidentStore>(), sp.GetRequiredService<IEventPublisher>(), sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NotifyingIncidentStore>>()));
        services.AddSingleton<ISpendLedger, PostgresSpendLedger>();
        services.AddSingleton<DiagnosisQueue>();
        services.AddSingleton<IDiagnosisQueue>(sp => sp.GetRequiredService<DiagnosisQueue>());
        services.AddSingleton<IPlatformInspector, HttpPlatformInspector>();
        services.AddSingleton<IRemediationExecutor, HttpRemediationExecutor>();
        services.AddSingleton<IFaultDriver, FaultDriver>();

        AddEmbeddings(services, configuration);
        AddLanguageModel(services, configuration);
        AddPlatformClients(services, configuration);

        if (configuration.GetValue("Steward:RunDetectors", true))
        {
            services.AddKafkaConsumer<StuckCouponV1, StuckCouponObserver>(Topics.StuckCoupon, "swiftbets.steward.stuck", startAtLatest: true);
            services.AddKafkaConsumer<CouponSettledV2, CouponSettledObserver>(Topics.CouponSettledV2, "swiftbets.steward.settled-v2", startAtLatest: true);
            services.AddKafkaConsumer<PaymentDriftDetectedV1, PaymentDriftObserver>(Topics.PaymentDriftDetected, "swiftbets.steward.payment-drift", startAtLatest: true);
            services.AddKafkaConsumer<ProviderReconciliationV1, ProviderDriftObserver>(Topics.ProviderReconciliation, "swiftbets.steward.provider-drift", startAtLatest: true);
            AddLog<ResultPublishedV1>(services, Topics.ResultPublished, r => r.FixtureId);
            AddLog<PayoutCompletedV1>(services, Topics.PayoutCompleted, p => p.CouponId.ToString());
            AddLog<PayoutAttemptV1>(services, Topics.PayoutDeadLetter, p => p.CouponId.ToString());
            services.AddHostedService<DeadLetterWatcher>();
            services.AddHostedService<WalletOutageProbe>();
            services.AddHostedService<DiagnosisWorker>();
        }

        services.AddHostedService<HousekeepingWorker>();
        AddStatusPage(services, configuration);
        return services;
    }

    private static void AddStatusPage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<StatusOptions>(configuration, StatusOptions.SectionName);
        services.AddSingleton<IStatusStore, PostgresStatusStore>();
        services.AddSingleton<ComponentStatusBoard>();
        services.AddSingleton<IComponentStatus>(sp => sp.GetRequiredService<ComponentStatusBoard>());
        services.AddHttpClient(StatusPoller.ClientName, http => http.Timeout = TimeSpan.FromSeconds(5));
        services.AddSingleton<StatusPoller>();
        services.AddHostedService(sp => sp.GetRequiredService<StatusPoller>());
    }

    private static void AddLog<T>(IServiceCollection services, string topic, Func<T, string> key)
        where T : IEventContract
    {
        services.AddScoped<IEventHandler<T>>(sp => new EventLogObserver<T>(sp.GetRequiredService<PostgresEventLog>(), key, topic));
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp => new KafkaConsumerHost<T>(
            new ConsumerRegistration(topic, $"swiftbets.steward.log.{topic}", StartAtLatest: true),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<IOptions<KafkaOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<KafkaConsumerHost<T>>>()));
    }

    private static void AddEmbeddings(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Steward:Embeddings:Provider"] ?? "hashing";
        if (provider == "ollama")
        {
            var address = Required(configuration, "Steward:Embeddings:OllamaAddress");
            services.AddHttpClient("ollama", http => http.BaseAddress = new Uri(address.TrimEnd('/') + "/")).AddIdempotentResilience();
            services.AddSingleton<IEmbeddingGenerator>(sp => new OllamaEmbeddingGenerator(sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama"), configuration["Steward:Embeddings:Model"] ?? "nomic-embed-text"));
        }
        else
        {
            services.AddSingleton<IEmbeddingGenerator, HashingEmbeddingGenerator>();
        }

        var floor = configuration.GetValue("Steward:Embeddings:MinimumSimilarity", provider == "ollama" ? 0.55 : 0.99);
        services.AddSingleton(sp => new PgvectorRunbookStore(sp.GetRequiredService<NpgsqlDataSource>(), sp.GetRequiredService<IEmbeddingGenerator>(), floor));
        services.AddSingleton<IRunbookSearch>(sp => sp.GetRequiredService<PgvectorRunbookStore>());
    }

    /// <summary>anthropic calls the API (optionally recording transcripts); replay plays recorded transcripts and never calls out.</summary>
    private static void AddLanguageModel(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Steward:ModelProvider:Provider"] ?? "replay";
        services.AddSingleton<ILanguageModel>(sp =>
        {
            ILanguageModel model = provider switch
            {
                "anthropic" => new AnthropicLanguageModel(
                    new AnthropicClient { ApiKey = configuration["ANTHROPIC_API_KEY"] is { Length: > 0 } key ? key : throw new InvalidOperationException("Steward:ModelProvider:Provider is anthropic but ANTHROPIC_API_KEY is not set.") },
                    sp.GetRequiredService<IOptions<StewardOptions>>().Value.Model),
                "replay" => new ReplayLanguageModel(Required(configuration, "Steward:ModelProvider:ReplayDirectory")),
                "disabled" => new DisabledLanguageModel(),
                _ => throw new InvalidOperationException($"Unknown Steward:ModelProvider:Provider '{provider}'."),
            };
            // Scrubbing sits outermost, so neither the provider nor a recorded transcript ever sees personal data.
            var recorded = configuration["Steward:ModelProvider:RecordDirectory"] is { Length: > 0 } record ? new RecordingLanguageModel(model, record) : model;
            return new ScrubbingLanguageModel(recorded);
        });
    }

    private static void AddPlatformClients(IServiceCollection services, IConfiguration configuration)
    {
        services.AddClientCredentials(configuration);
        services.AddTransient<ServiceTokenHandler>();
        void Add(string name, Func<PlatformOptions, string> address) =>
            services.AddHttpClient(name, (sp, http) =>
            {
                http.BaseAddress = new Uri(address(sp.GetRequiredService<IOptions<PlatformOptions>>().Value).TrimEnd('/') + "/");
                http.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<ServiceTokenHandler>()
            .AddKeyedResilience();

        Add(HttpPlatformInspector.Settlement, o => o.SettlementAddress);
        Add(HttpPlatformInspector.Payout, o => o.PayoutAddress);
        Add("offer", o => o.OfferAddress);
        Add("wallet", o => o.WalletAddress);
        Add(HttpPlatformInspector.Casino, o => o.CasinoAddress);
        Add(HttpPlatformInspector.Payments, o => o.PaymentsAddress);
        Add(HttpPlatformInspector.Config, o => o.ConfigAddress);
        services.AddHttpClient(HttpPlatformInspector.Prometheus, (sp, http) => http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<PlatformOptions>>().Value.PrometheusAddress.TrimEnd('/') + "/"))
            .AddIdempotentResilience();
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configuration '{key}' is required.");
}
