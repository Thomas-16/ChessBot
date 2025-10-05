using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using ChessChallenge.Application.Config;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application.Services
{
    public class SignalRService : IDisposable
    {
        private readonly BackendConfig _config;
        private HubConnection? _connection;
        private bool _isConnected;
        private bool _isConnecting;

        public bool IsConnected => _isConnected;

        public SignalRService(BackendConfig config)
        {
            _config = config;
        }

        public async Task<bool> ConnectAsync()
        {
            if (!_config.EnableBackendIntegration)
            {
                Log("📴 Backend integration disabled", false, ConsoleColor.Gray);
                return false;
            }

            if (_isConnected || _isConnecting)
            {
                return _isConnected;
            }

            _isConnecting = true;

            try
            {
                string hubUrl = $"{_config.ApiBaseUrl.TrimEnd('/')}/chesshub";
                Log($"🔌 Connecting to SignalR hub: {hubUrl}", false, ConsoleColor.Cyan);

                _connection = new HubConnectionBuilder()
                    .WithUrl(hubUrl)
                    .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                    .Build();

                // Setup event handlers BEFORE starting connection
                _connection.Closed += OnDisconnected;
                _connection.Reconnecting += OnReconnecting;
                _connection.Reconnected += OnReconnected;

                // Setup server event handlers
                _connection.On<string>("GameSaved", OnGameSaved);
                _connection.On<string>("SaveError", OnSaveError);

                // Register chat handlers if they were registered before connection was established
                if (_chatResponseHandler != null)
                {
                    _connection.On<object>("ReceiveChatResponse", _chatResponseHandler);
                }
                if (_chatErrorHandler != null)
                {
                    _connection.On<string>("ChatError", _chatErrorHandler);
                }
                if (_audioChunkHandler != null)
                {
                    _connection.On<object>("ReceiveAudioChunk", _audioChunkHandler);
                }

                await _connection.StartAsync();
                _isConnected = true;
                _isConnecting = false;

                Log("✅ SignalR connection established", false, ConsoleColor.Green);
                return true;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _isConnecting = false;
                Log($"❌ SignalR connection failed: {ex.Message}", false, ConsoleColor.Red);
                return false;
            }
        }

        public async Task<string?> StartNewGameAsync()
        {
            if (!_isConnected || _connection == null)
            {
                Log("⚠️ Cannot start game - SignalR not connected", false, ConsoleColor.Yellow);
                return null;
            }

            try
            {
                Log("🎮 Creating new game session via SignalR...", false, ConsoleColor.Cyan);
                string gameId = await _connection.InvokeAsync<string>("StartNewGame");
                Log($"✅ Game session created: {gameId}", false, ConsoleColor.Green);
                return gameId;
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to create game session: {ex.Message}", false, ConsoleColor.Red);
                return null;
            }
        }

        public async Task<bool> SaveGameResultAsync(string gameId, string pgn, string result)
        {
            if (!_isConnected || _connection == null)
            {
                Log("⚠️ Cannot save game - SignalR not connected", false, ConsoleColor.Yellow);
                return false;
            }

            try
            {
                Log($"💾 Saving game result via SignalR: {gameId}", false, ConsoleColor.Cyan);
                await _connection.InvokeAsync("SaveGameResult", gameId, pgn, result);
                Log($"✅ Game save request sent for: {gameId}", false, ConsoleColor.Green);
                return true;
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to save game: {ex.Message}", false, ConsoleColor.Red);
                return false;
            }
        }

        public async Task<bool> SendChatMessageAsync(string gameId, string message)
        {
            if (!_isConnected || _connection == null)
            {
                Log("⚠️ Cannot send chat message - SignalR not connected", false, ConsoleColor.Yellow);
                return false;
            }

            try
            {
                Log($"💬 Sending chat message via SignalR: {gameId}", false, ConsoleColor.Cyan);
                await _connection.InvokeAsync("SendChatMessage", gameId, message);
                return true;
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to send chat message: {ex.Message}", false, ConsoleColor.Red);
                return false;
            }
        }

        private Action<object>? _chatResponseHandler;
        private Action<string>? _chatErrorHandler;
        private Action<object>? _audioChunkHandler;

        public void RegisterChatResponseHandler(Action<object> handler)
        {
            _chatResponseHandler = handler;
            if (_connection != null)
            {
                _connection.On<object>("ReceiveChatResponse", handler);
            }
        }

        public void RegisterChatErrorHandler(Action<string> handler)
        {
            _chatErrorHandler = handler;
            if (_connection != null)
            {
                _connection.On<string>("ChatError", handler);
            }
        }

        public void RegisterAudioChunkHandler(Action<object> handler)
        {
            _audioChunkHandler = handler;
            if (_connection != null)
            {
                _connection.On<object>("ReceiveAudioChunk", handler);
            }
        }

        public HubConnection? GetConnection()
        {
            return _connection;
        }

        public async Task DisconnectAsync()
        {
            if (_connection != null)
            {
                Log("🔌 Disconnecting from SignalR hub...", false, ConsoleColor.Cyan);
                await _connection.StopAsync();
                await _connection.DisposeAsync();
                _connection = null;
                _isConnected = false;
                _isConnecting = false;
            }
        }

        private Task OnDisconnected(Exception? exception)
        {
            _isConnected = false;
            _isConnecting = false;

            if (exception != null)
            {
                Log($"📴 SignalR disconnected: {exception.Message}", false, ConsoleColor.Yellow);
            }
            else
            {
                Log("📴 SignalR disconnected", false, ConsoleColor.Gray);
            }

            return Task.CompletedTask;
        }

        private Task OnReconnecting(Exception? exception)
        {
            _isConnected = false;
            _isConnecting = true;
            Log("🔄 SignalR reconnecting...", false, ConsoleColor.Yellow);
            return Task.CompletedTask;
        }

        private Task OnReconnected(string? connectionId)
        {
            _isConnected = true;
            _isConnecting = false;
            Log($"✅ SignalR reconnected: {connectionId}", false, ConsoleColor.Green);
            return Task.CompletedTask;
        }

        private void OnGameSaved(string gameId)
        {
            Log($"✅ Game saved successfully: {gameId}", false, ConsoleColor.Green);
        }

        private void OnSaveError(string error)
        {
            Log($"❌ Game save error: {error}", false, ConsoleColor.Red);
        }

        public void Dispose()
        {
            if (_connection != null)
            {
                try
                {
                    _connection.StopAsync().Wait(5000);
                    _connection.DisposeAsync().AsTask().Wait(5000);
                }
                catch (Exception ex)
                {
                    Log($"⚠️ Error disposing SignalR connection: {ex.Message}", false, ConsoleColor.Yellow);
                }
                finally
                {
                    _connection = null;
                    _isConnected = false;
                    _isConnecting = false;
                }
            }
        }
    }
}