using System;
using System.Threading.Tasks;
using ChessChallenge.Chess;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application.Services
{
    public class GameStorageService
    {
        private readonly BackendApiService _backendApi;
        private string? _currentGameId;

        public GameStorageService(BackendApiService backendApi)
        {
            _backendApi = backendApi;
        }

        public async Task<bool> InitializeConnectionAsync()
        {
            Log("🔌 Initializing backend connection...", false, ConsoleColor.Cyan);
            var connected = await _backendApi.CheckConnectionAsync();

            // If we have no current game but connection just established, try to start a new session
            if (connected && string.IsNullOrEmpty(_currentGameId))
            {
                Log("🔄 Backend connected - attempting to create game session...", false, ConsoleColor.Cyan);
                _ = Task.Run(async () =>
                {
                    await StartNewGameAsync();
                });
            }

            return connected;
        }

        public async Task<string?> StartNewGameAsync()
        {
            Log($"🎯 StartNewGameAsync called. Backend connected: {_backendApi.IsConnected}", false, ConsoleColor.Cyan);

            try
            {
                _currentGameId = await _backendApi.StartNewGameAsync();

                Log($"🔍 Backend API returned gameId: '{_currentGameId}'", false, ConsoleColor.Magenta);

                if (!string.IsNullOrEmpty(_currentGameId))
                {
                    Log($"🎮 New game session started: {_currentGameId}", false, ConsoleColor.Green);
                }
                else if (_backendApi.IsConnected)
                {
                    Log("⚠️ Failed to start backend game session, continuing locally", false, ConsoleColor.Yellow);
                }
                else
                {
                    Log("📴 Backend not connected - no game session created", false, ConsoleColor.Gray);
                }

                return _currentGameId;
            }
            catch (Exception ex)
            {
                Log($"❌ Error starting game session: {ex.Message}", false, ConsoleColor.Red);
                return null;
            }
        }

        public async Task<bool> SaveGameAsync(Board board, GameResult result, string whiteName, string blackName)
        {
            Log($"🔍 SaveGameAsync called. CurrentGameId: '{_currentGameId}'", false, ConsoleColor.Magenta);
            Log($"🔍 Backend API connected: {_backendApi.IsConnected}", false, ConsoleColor.Magenta);

            if (string.IsNullOrEmpty(_currentGameId))
            {
                Log($"❌ No active game session - cannot save game", false, ConsoleColor.Red);
                return false;
            }

            try
            {
                // Generate PGN using existing PGNCreator
                string pgn = PGNCreator.CreatePGN(board, result, whiteName, blackName);
                string resultString = ConvertGameResultToString(result);

                bool success = await _backendApi.SaveGameResultAsync(_currentGameId, pgn, resultString);

                if (success)
                {
                    Log($"💾 Game saved successfully: {_currentGameId}", false, ConsoleColor.Green);
                }
                else if (_backendApi.IsConnected)
                {
                    Log($"⚠️ Failed to save game: {_currentGameId}", false, ConsoleColor.Yellow);
                }

                // Clear current game ID after saving (successful or not)
                _currentGameId = null;
                return success;
            }
            catch (Exception ex)
            {
                Log($"❌ Error saving game: {ex.Message}", false, ConsoleColor.Red);
                _currentGameId = null;
                return false;
            }
        }

        public void ClearCurrentGame()
        {
            _currentGameId = null;
        }

        private static string ConvertGameResultToString(GameResult result)
        {
            return result switch
            {
                GameResult.WhiteIsMated => "0-1",
                GameResult.BlackIsMated => "1-0",
                GameResult.Stalemate => "1/2-1/2",
                GameResult.Repetition => "1/2-1/2",
                GameResult.FiftyMoveRule => "1/2-1/2",
                GameResult.InsufficientMaterial => "1/2-1/2",
                GameResult.DrawByArbiter => "1/2-1/2",
                GameResult.WhiteTimeout => "0-1",
                GameResult.BlackTimeout => "1-0",
                GameResult.WhiteIllegalMove => "0-1",
                GameResult.BlackIllegalMove => "1-0",
                _ => "*"
            };
        }
    }
}