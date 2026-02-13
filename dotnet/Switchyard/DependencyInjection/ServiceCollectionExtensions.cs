using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Switchyard.Config;
using Switchyard.Dispatch;
using Switchyard.Health;
using Switchyard.Interpreter;
using Switchyard.Transport;
using Switchyard.Tts;

namespace Switchyard.DependencyInjection;

/// <summary>Clean DI registration for all Switchyard services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers Switchyard configuration with the Options pattern, validation, and post-configure.</summary>
    public static IServiceCollection AddSwitchyardOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SwitchyardOptions>()
            .Bind(configuration.GetSection(SwitchyardOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPostConfigureOptions<SwitchyardOptions>, SwitchyardOptionsPostConfigure>();

        return services;
    }

    /// <summary>Registers the interpreter pipeline (OpenAI or Local) using keyed services.</summary>
    public static IServiceCollection AddSwitchyardInterpreter(this IServiceCollection services)
    {
        // Register typed HTTP clients for each interpreter backend.
        services.AddHttpClient<OpenAIInterpreter>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Switchyard/1.0");
        }).AddStandardResilienceHandler();

        services.AddHttpClient<LocalInterpreter>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Switchyard/1.0");
        }).AddStandardResilienceHandler();

        // Keyed service registrations.
        services.AddKeyedSingleton<IInterpreter>("openai",
            (sp, _) => sp.GetRequiredService<OpenAIInterpreter>());
        services.AddKeyedSingleton<IInterpreter>("local",
            (sp, _) => sp.GetRequiredService<LocalInterpreter>());

        // Register concrete implementations so typed HttpClients resolve them.
        services.AddSingleton<OpenAIInterpreter>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SwitchyardOptions>>().Value;
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenAIInterpreter));
            return new OpenAIInterpreter(opts.Interpreter.OpenAI, client,
                sp.GetRequiredService<ILogger<OpenAIInterpreter>>());
        });

        services.AddSingleton<LocalInterpreter>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SwitchyardOptions>>().Value;
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LocalInterpreter));
            return new LocalInterpreter(opts.Interpreter.Local, client,
                sp.GetRequiredService<ILogger<LocalInterpreter>>());
        });

        // Primary IInterpreter — resolved by config backend value.
        services.AddSingleton<IInterpreter>(sp =>
        {
            var backend = sp.GetRequiredService<IOptions<SwitchyardOptions>>().Value
                .Interpreter.Backend.ToLowerInvariant();
            return sp.GetRequiredKeyedService<IInterpreter>(backend);
        });

        return services;
    }

    /// <summary>Registers TTS synthesis (conditional on config).</summary>
    public static IServiceCollection AddSwitchyardTts(this IServiceCollection services)
    {
        services.AddSingleton<ISynthesizer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SwitchyardOptions>>().Value;
            if (!opts.Tts.Enabled)
                return NullSynthesizer.Instance;

            return opts.Tts.Backend.ToLowerInvariant() switch
            {
                "piper" => new PiperSynthesizer(opts.Tts.Piper,
                    sp.GetRequiredService<ILogger<PiperSynthesizer>>()),
                _ => throw new InvalidOperationException($"Unknown TTS backend: {opts.Tts.Backend}")
            };
        });

        return services;
    }

    /// <summary>Registers message transports (gRPC, MQTT).</summary>
    public static IServiceCollection AddSwitchyardTransports(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ITransport>("grpc", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<SwitchyardOptions>>().Value;
            return new GrpcTransport(opts.Transports.Grpc.Port,
                sp.GetRequiredService<ILogger<GrpcTransport>>());
        });

        services.AddKeyedSingleton<ITransport>("mqtt", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<SwitchyardOptions>>().Value;
            return new MqttTransport(opts.Transports.Mqtt.Broker, opts.Transports.Mqtt.Topic,
                sp.GetRequiredService<ILogger<MqttTransport>>());
        });

        return services;
    }

    /// <summary>Registers the central <see cref="IDispatcher"/> and its dependencies.</summary>
    public static IServiceCollection AddSwitchyardDispatcher(this IServiceCollection services)
    {
        services.AddSingleton<IDispatcher, Dispatcher>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SwitchyardOptions>>().Value;
            var interp = sp.GetRequiredService<IInterpreter>();
            var synth = sp.GetRequiredService<ISynthesizer>();
            var logger = sp.GetRequiredService<ILogger<Dispatcher>>();

            var transports = new List<ITransport>();
            if (opts.Transports.Grpc.Enabled)
                transports.Add(sp.GetRequiredKeyedService<ITransport>("grpc"));
            if (opts.Transports.Mqtt.Enabled)
                transports.Add(sp.GetRequiredKeyedService<ITransport>("mqtt"));

            return new Dispatcher(interp, transports, synth, logger);
        });

        return services;
    }

    /// <summary>Registers ASP.NET Core health checks for readiness probes.</summary>
    public static IServiceCollection AddSwitchyardHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<SwitchyardHealthCheck>("switchyard", tags: ["ready"]);

        return services;
    }
}
