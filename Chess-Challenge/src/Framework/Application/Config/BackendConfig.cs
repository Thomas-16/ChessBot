using System;
using Microsoft.Extensions.Configuration;

namespace ChessChallenge.Application.Config
{
    public class BackendConfig
    {
        public string ApiBaseUrl { get; set; } = "https://chessbot-oylr.onrender.com";
        public bool EnableBackendIntegration { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
        public int RetryAttempts { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;

        public static BackendConfig LoadFromConfiguration(IConfiguration? configuration)
        {
            var config = new BackendConfig();

            if (configuration != null)
            {
                var backendSection = configuration.GetSection("Backend");
                backendSection.Bind(config);
            }

            // Override with environment variables if they exist
            var envApiUrl = Environment.GetEnvironmentVariable("CHESS_BACKEND_URL");
            if (!string.IsNullOrEmpty(envApiUrl))
            {
                config.ApiBaseUrl = envApiUrl;
            }

            var envEnabled = Environment.GetEnvironmentVariable("CHESS_BACKEND_ENABLED");
            if (!string.IsNullOrEmpty(envEnabled) && bool.TryParse(envEnabled, out bool enabled))
            {
                config.EnableBackendIntegration = enabled;
            }

            return config;
        }

        public string GetHealthCheckUrl() => $"{ApiBaseUrl.TrimEnd('/')}/health/ready";
        public string GetStartGameUrl() => $"{ApiBaseUrl.TrimEnd('/')}/chesshub";
    }
}