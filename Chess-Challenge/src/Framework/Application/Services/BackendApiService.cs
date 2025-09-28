using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ChessChallenge.Application.Config;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application.Services
{
    public class BackendApiService
    {
        private readonly HttpClient _httpClient;
        private readonly BackendConfig _config;
        private bool _isConnected = false;

        public bool IsConnected => _isConnected;

        public BackendApiService(HttpClient httpClient, BackendConfig config)
        {
            _httpClient = httpClient;
            _config = config;
            _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
        }

        public async Task<bool> CheckConnectionAsync()
        {
            if (!_config.EnableBackendIntegration)
            {
                _isConnected = false;
                return false;
            }

            try
            {
                var response = await _httpClient.GetAsync(_config.GetHealthCheckUrl());
                _isConnected = response.IsSuccessStatusCode;

                if (_isConnected)
                {
                    Log("✅ Backend connection established", false, ConsoleColor.Green);
                }
                else
                {
                    Log($"⚠️ Backend health check failed: {response.StatusCode}", false, ConsoleColor.Yellow);
                }

                return _isConnected;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                Log($"⚠️ Backend connection failed: {ex.Message}", false, ConsoleColor.Yellow);
                return false;
            }
        }

        public async Task<string?> StartNewGameAsync()
        {
            if (!_config.EnableBackendIntegration || !_isConnected)
            {
                return null;
            }

            for (int attempt = 0; attempt < _config.RetryAttempts; attempt++)
            {
                try
                {
                    // For now, we'll use simple HTTP calls since SignalR integration would be more complex
                    // Generate a simple game ID locally for this initial implementation
                    var gameId = Guid.NewGuid().ToString();
                    Log($"🎮 Started new game session: {gameId}", false, ConsoleColor.Blue);
                    return gameId;
                }
                catch (Exception ex)
                {
                    Log($"❌ Failed to start game session (attempt {attempt + 1}): {ex.Message}", false, ConsoleColor.Red);

                    if (attempt < _config.RetryAttempts - 1)
                    {
                        await Task.Delay(_config.RetryDelayMs);
                    }
                }
            }

            return null;
        }

        public async Task<bool> SaveGameResultAsync(string gameId, string pgn, string result)
        {
            if (!_config.EnableBackendIntegration || !_isConnected || string.IsNullOrEmpty(gameId))
            {
                return false;
            }

            for (int attempt = 0; attempt < _config.RetryAttempts; attempt++)
            {
                try
                {
                    var gameData = new
                    {
                        gameId = gameId,
                        pgn = pgn,
                        result = result,
                        timestamp = DateTime.UtcNow
                    };

                    var jsonContent = JsonSerializer.Serialize(gameData);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    // For now, just log the game data since SignalR integration would require more setup
                    Log($"💾 Game completed: {gameId} | Result: {result}", false, ConsoleColor.Blue);
                    // Log($"📝 PGN: {pgn.Substring(0, Math.Min(100, pgn.Length))}...", false, ConsoleColor.Gray);

                    return true;
                }
                catch (Exception ex)
                {
                    Log($"❌ Failed to save game result (attempt {attempt + 1}): {ex.Message}", false, ConsoleColor.Red);

                    if (attempt < _config.RetryAttempts - 1)
                    {
                        await Task.Delay(_config.RetryDelayMs);
                    }
                }
            }

            return false;
        }
    }
}