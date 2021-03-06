﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoFabric.Docimax.Core.Utils;
using NanoFabric.Docimax.Grains.Contracts.Heroes;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using SignalR.Orleans;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace NanoFabric.Docimax.Heroes.Api.Infrastructure
{

    public static class ClientBuilderExtensions
    {
        public static IServiceCollection UseOrleansClient(this IServiceCollection services, ClientBuilderContext context)
        {
              
            if (context == null)
                throw new ArgumentNullException($"{nameof(context)}");
            if (context.AppInfo == null)
                throw new ArgumentNullException($"{nameof(context.AppInfo)}");

            try
            {
                Console.WriteLine("Client cluster connecting to silo {0}", context.ClusterId);

                var client = InitializeWithRetries(context).Result;
                services.AddSingleton(client);
                services.AddSignalR().AddOrleans(new SignalRClusterClientProvider(client));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Orleans client initialization failed failed due to {ex}");

                Console.ReadLine();
            }
            return services;
        }

        private static async Task<IClusterClient> InitializeWithRetries(ClientBuilderContext context)
        {
            var attempt = 0;
            var stopwatch = Stopwatch.StartNew();
            var clientClusterConfig = new ClientConfiguration();

            await Task.Delay(TimeSpan.FromSeconds(clientClusterConfig.DelayInitialConnectSeconds));

            var clientConfig = new ClientBuilder()
                .UseConfiguration(context);

            context.ConfigureClientBuilder?.Invoke(clientConfig);

            var client = clientConfig.Build();
            await client.Connect(async ex =>
            {
                attempt++;
                if (attempt > clientClusterConfig.ConnectionRetry.TotalRetries)
                {
                    Console.WriteLine(ex.Message);
                    return false;
                }

                var delay = RandomUtils.GenerateNumber(clientClusterConfig.ConnectionRetry.MinDelaySeconds, clientClusterConfig.ConnectionRetry.MaxDelaySeconds);
                Console.WriteLine("Client cluster {0} failed to connect. Attempt {1} of {2}. Retrying in {3}s.",
                    context.ClusterId, attempt, clientClusterConfig.ConnectionRetry.TotalRetries, delay);
                await Task.Delay(TimeSpan.FromSeconds(delay));
                return true;
            });

            Console.WriteLine("Client cluster connected successfully to silo {0} in {1:#.##}s.",
                context.ClusterId, stopwatch.Elapsed.TotalSeconds);
            return client;
        }

        public static IClientBuilder UseConfiguration(
            this IClientBuilder clientBuilder,
            ClientBuilderContext context
        )
        {
           return clientBuilder
            .Configure<ClusterOptions>(config =>
            {
                config.ClusterId = "dev";
                config.ServiceId = "Heroes";
            })
            .UseConsulClustering(options => {
                options.Address = new Uri(context.ConsulEndPoint);
            })
            .ConfigureApplicationParts(parts => parts.AddApplicationPart(typeof(IHeroGrain).Assembly).WithReferences())
             .ConfigureLogging(logging => logging.AddConsole());
        }

    }

    public class SignalRClusterClientProvider : IClusterClientProvider
    {
        private IClusterClient _clusterClient;

        public SignalRClusterClientProvider(IClusterClient clusterClient)
        {
            this._clusterClient = clusterClient;
        }

        public IClusterClient GetClient() => this._clusterClient;
    }
}
