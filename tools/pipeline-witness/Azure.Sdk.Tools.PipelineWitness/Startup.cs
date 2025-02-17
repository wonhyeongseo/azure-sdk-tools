﻿using System;
using Azure.Identity;
using Azure.Sdk.Tools.PipelineWitness.ApplicationInsights;
using Azure.Sdk.Tools.PipelineWitness.Services;
using Azure.Sdk.Tools.PipelineWitness.Services.FailureAnalysis;
using Azure.Security.KeyVault.Secrets;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.TestResults.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace Azure.Sdk.Tools.PipelineWitness
{
    public static class Startup
    {
        public static void Configure(WebApplicationBuilder builder)
        {
            var settings = new PipelineWitnessSettings();
            var settingsSection = builder.Configuration.GetSection("PipelineWitness");
            settingsSection.Bind(settings);

            builder.Services.AddApplicationInsightsTelemetry(builder.Configuration);
            builder.Services.AddApplicationInsightsTelemetryProcessor<BlobNotFoundTelemetryProcessor>();
            builder.Services.AddTransient<ITelemetryInitializer, ApplicationVersionTelemetryInitializer>();

            builder.Services.AddAzureClients(builder =>
            {
                builder.UseCredential(new DefaultAzureCredential());
                builder.AddSecretClient(new Uri(settings.KeyVaultUri));
                builder.AddBlobServiceClient(new Uri(settings.BlobStorageAccountUri));
                builder.AddQueueServiceClient(new Uri(settings.QueueStorageAccountUri))
                    .ConfigureOptions(o => o.MessageEncoding = Storage.Queues.QueueMessageEncoding.Base64);
            });

            builder.Services.AddSingleton<VssConnection>(provider =>
            {
                var secretClient = provider.GetRequiredService<SecretClient>();
                KeyVaultSecret secret = secretClient.GetSecret("azure-devops-personal-access-token");
                var credential = new VssBasicCredential("nobody", secret.Value);
                var connection = new VssConnection(new Uri("https://dev.azure.com/azure-sdk"), credential);
                return connection;
            });

            builder.Services.AddSingleton(provider => provider.GetRequiredService<VssConnection>().GetClient<ProjectHttpClient>());
            builder.Services.AddSingleton(provider => provider.GetRequiredService<VssConnection>().GetClient<BuildHttpClient>());
            builder.Services.AddSingleton(provider => provider.GetRequiredService<VssConnection>().GetClient<TestResultsHttpClient>());

            builder.Services.AddLogging();
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<BlobUploadProcessor>();
            builder.Services.AddSingleton<BuildLogProvider>();
            builder.Services.AddSingleton<IFailureAnalyzer, FailureAnalyzer>();
            builder.Services.AddSingleton<IFailureClassifier, AzuriteInstallFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, CancelledTaskClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, CosmosDbEmulatorStartFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, AzurePipelinesPoolOutageClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, PythonPipelineTestFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, JavaScriptLiveTestFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, TestResourcesDeploymentFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, DotnetPipelineTestFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, JavaPipelineTestFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, JsSamplesExecutionFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, JsDevFeedPublishingFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, DownloadSecretsFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, GitCheckoutFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, AzuriteInstallFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, MavenBrokenPipeFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, CodeSigningFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, AzureArtifactsServiceUnavailableClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, DnsResolutionFailureClassifier>();
            builder.Services.AddSingleton<IFailureClassifier, CacheFailureClassifier>();
            builder.Services.Configure<PipelineWitnessSettings>(settingsSection);

            builder.Services.AddHostedService<BuildCompleteQueueWorker>(settings.BuildCompleteWorkerCount);
            builder.Services.AddHostedService<BuildLogBundleQueueWorker>(settings.BuildLogBundlesWorkerCount);
            builder.Services.AddHostedService<AzurePipelinesBuildDefinitionWorker>();
        }

        private static void AddHostedService<T>(this IServiceCollection services, int instanceCount) where T : class, IHostedService
        {
            for (var i = 0; i < instanceCount; i++)
            {
                services.AddSingleton<IHostedService, T>();
            }
        }
    }
}
