using System;
using Microsoft.Extensions.Configuration;

namespace ChessChallenge.Application.Config
{
    public class BackendConfig
    {
        public string ApiBaseUrl { get; set; } = "https://chessbot-oylr.onrender.com";
        public bool EnableBackendIntegration { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;

        public static BackendConfig LoadFromConfiguration(IConfiguration? configuration)
        {
            BackendConfig config = new BackendConfig();

            if (configuration != null)
            {
                IConfigurationSection? backendSection = configuration.GetSection("Backend");
                backendSection.Bind(config);
            }

            // Override with environment variables if they exist
            string? envApiUrl = Environment.GetEnvironmentVariable("CHESS_BACKEND_URL");
            if (!string.IsNullOrEmpty(envApiUrl))
            {
                config.ApiBaseUrl = envApiUrl;
            }

            string? envEnabled = Environment.GetEnvironmentVariable("CHESS_BACKEND_ENABLED");
            if (!string.IsNullOrEmpty(envEnabled) && bool.TryParse(envEnabled, out bool enabled))
            {
                config.EnableBackendIntegration = enabled;
            }

            return config;
        }
    }
}