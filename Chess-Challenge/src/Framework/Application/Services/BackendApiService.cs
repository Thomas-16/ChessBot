using System;
using System.Net.Http;
using System.Threading.Tasks;
using ChessChallenge.Application.Config;
using ChessChallenge.Application.Models;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application.Services
{
    public class BackendApiService
    {
        private readonly HttpClient _httpClient;
        private readonly SignalRService _signalRService;
        private readonly BackendConfig _config;

        public bool IsConnected => _signalRService.IsConnected;

        public BackendApiService(HttpClient httpClient, SignalRService signalRService, BackendConfig config)
        {
            _httpClient = httpClient;
            _signalRService = signalRService;
            _config = config;
            _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
        }

        public async Task<bool> CheckConnectionAsync()
        {
            if (!_config.EnableBackendIntegration)
            {
                return false;
            }

            // Use SignalR connection
            return await _signalRService.ConnectAsync();
        }

        public async Task<string?> StartNewGameAsync()
        {
            if (!_config.EnableBackendIntegration || !IsConnected)
            {
                return null;
            }

            // Use SignalR hub method
            return await _signalRService.StartNewGameAsync();
        }

        public async Task DisconnectAsync()
        {
            if (!_config.EnableBackendIntegration || !IsConnected)
            {
                return;
            }
            
            await _signalRService.DisconnectAsync();
        }

        public async Task<bool> SaveGameResultAsync(string gameId, string pgn, string result)
        {
            if (!_config.EnableBackendIntegration || !IsConnected || string.IsNullOrEmpty(gameId))
            {
                return false;
            }

            // Use SignalR hub method
            return await _signalRService.SaveGameResultAsync(gameId, pgn, result);
        }

        public async Task<bool> SendChatMessageAsync(string gameId, string message, ChessPositionContext? context = null)
        {
            if (!_config.EnableBackendIntegration || !IsConnected || string.IsNullOrEmpty(gameId))
            {
                return false;
            }

            return await _signalRService.SendChatMessageAsync(gameId, message, context);
        }

        public void RegisterChatResponseHandler(Action<object> handler)
        {
            _signalRService.RegisterChatResponseHandler(handler);
        }

        public void RegisterChatErrorHandler(Action<string> handler)
        {
            _signalRService.RegisterChatErrorHandler(handler);
        }
    }
}