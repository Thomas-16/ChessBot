using ChessChallenge.Chess;
using ChessChallenge.Example;
using Raylib_cs;
using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChessChallenge.Application.Services;
using ChessChallenge.Application.Models;
using Microsoft.Extensions.DependencyInjection;
using static ChessChallenge.Application.Settings;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application
{
    public class ChallengeController
    {
        public enum PlayerType
        {
            Human,
            MyBot,
            EvilBot
        }

        // Game state
        readonly Random rng;
        int gameID;
        bool isPlaying;
        Board board;
        public ChessPlayer PlayerWhite { get; private set; }
        public ChessPlayer PlayerBlack {get;private set;}

        float lastMoveMadeTime;
        bool isWaitingToPlayMove;
        Move moveToPlay;
        float playMoveTime;
        public bool HumanWasWhiteLastGame { get; private set; }

        // Bot match state
        readonly string[] botMatchStartFens;
        int botMatchGameIndex;
        public BotMatchStats BotStatsA { get; private set; }
        public BotMatchStats BotStatsB {get;private set;}
        bool botAPlaysWhite;


        // Bot task
        AutoResetEvent botTaskWaitHandle;
        bool hasBotTaskException;
        ExceptionDispatchInfo botExInfo;

        // Other
        readonly BoardUI boardUI;
        readonly MoveGenerator moveGenerator;
        readonly int tokenCount;
        readonly int debugTokenCount;
        readonly StringBuilder pgns;

        // UI
        readonly ChatUI chatUI;

        // Backend integration
        readonly GameStorageService? gameStorage;
        readonly BackendApiService? backendApi;
        readonly AudioService? audioService;

        public ChallengeController()
        {
            Log($"Launching Chess-Challenge version {Settings.Version}");
            (tokenCount, debugTokenCount) = GetTokenCount();
            Warmer.Warm();

            rng = new Random();
            moveGenerator = new();
            boardUI = new BoardUI();
            board = new Board();
            pgns = new();

            // Initialize backend integration
            gameStorage = Program.GameStorage;
            backendApi = Program.ServiceProvider?.GetService<BackendApiService>();
            audioService = Program.ServiceProvider?.GetService<AudioService>();

            // Initialize ChatUI with services and context extractor
            chatUI = new ChatUI(gameStorage, backendApi, audioService, GetCurrentPositionContext);

            BotStatsA = new BotMatchStats("IBot");
            BotStatsB = new BotMatchStats("IBot");
            botMatchStartFens = FileHelper.ReadResourceFile("Fens.txt").Split('\n').Where(fen => fen.Length > 0).ToArray();
            botTaskWaitHandle = new AutoResetEvent(false);

            StartNewGame(PlayerType.Human, PlayerType.MyBot);
        }

        public void StartNewGame(PlayerType whiteType, PlayerType blackType)
        {
            // End any ongoing game
            EndGame(GameResult.DrawByArbiter, log: false, autoStartNextBotMatch: false);
            gameID = rng.Next();

            // Start new game session in backend
            _ = Task.Run(async () =>
            {
                if (gameStorage != null)
                {
                    await gameStorage.StartNewGameAsync();
                }
            });

            // Stop prev task and create a new one
            if (RunBotsOnSeparateThread)
            {
                // Allow task to terminate
                botTaskWaitHandle.Set();
                // Create new task
                botTaskWaitHandle = new AutoResetEvent(false);
                Task.Factory.StartNew(BotThinkerThread, TaskCreationOptions.LongRunning);
            }
            // Board Setup
            board = new Board();
            bool isGameWithHuman = whiteType is PlayerType.Human || blackType is PlayerType.Human;
            int fenIndex = isGameWithHuman ? 0 : botMatchGameIndex / 2;
            board.LoadPosition(botMatchStartFens[fenIndex]);

            // Player Setup
            PlayerWhite = CreatePlayer(whiteType);
            PlayerBlack = CreatePlayer(blackType);
            PlayerWhite.SubscribeToMoveChosenEventIfHuman(OnMoveChosen);
            PlayerBlack.SubscribeToMoveChosenEventIfHuman(OnMoveChosen);

            // UI Setup
            boardUI.UpdatePosition(board);
            boardUI.ResetSquareColours();
            SetBoardPerspective();

            // Start
            isPlaying = true;
            NotifyTurnToMove();
        }

        void BotThinkerThread()
        {
            int threadID = gameID;
            //Console.WriteLine("Starting thread: " + threadID);

            while (true)
            {
                // Sleep thread until notified
                botTaskWaitHandle.WaitOne();
                // Get bot move
                if (threadID == gameID)
                {
                    var move = GetBotMove();

                    if (threadID == gameID)
                    {
                        OnMoveChosen(move);
                    }
                }
                // Terminate if no longer playing this game
                if (threadID != gameID)
                {
                    break;
                }
            }
            //Console.WriteLine("Exitting thread: " + threadID);
        }

        Move GetBotMove()
        {
            API.Board botBoard = new(board);
            try
            {
                API.Timer timer = new(PlayerToMove.TimeRemainingMs, PlayerNotOnMove.TimeRemainingMs, GameDurationMilliseconds, IncrementMilliseconds);
                API.Move move = PlayerToMove.Bot.Think(botBoard, timer);
                return new Move(move.RawValue);
            }
            catch (Exception e)
            {
                Log("An error occurred while bot was thinking.\n" + e.ToString(), true, ConsoleColor.Red);
                hasBotTaskException = true;
                botExInfo = ExceptionDispatchInfo.Capture(e);
            }
            return Move.NullMove;
        }



        void NotifyTurnToMove()
        {
            //playerToMove.NotifyTurnToMove(board);
            if (PlayerToMove.IsHuman)
            {
                PlayerToMove.Human.SetPosition(FenUtility.CurrentFen(board));
                PlayerToMove.Human.NotifyTurnToMove();
            }
            else
            {
                if (RunBotsOnSeparateThread)
                {
                    botTaskWaitHandle.Set();
                }
                else
                {
                    double startThinkTime = Raylib.GetTime();
                    var move = GetBotMove();
                    double thinkDuration = Raylib.GetTime() - startThinkTime;
                    PlayerToMove.UpdateClock(thinkDuration);
                    OnMoveChosen(move);
                }
            }
        }

        void SetBoardPerspective()
        {
            // Board perspective
            if (PlayerWhite.IsHuman || PlayerBlack.IsHuman)
            {
                boardUI.SetPerspective(PlayerWhite.IsHuman);
                HumanWasWhiteLastGame = PlayerWhite.IsHuman;
            }
            else if (PlayerWhite.Bot is MainBot && PlayerBlack.Bot is MainBot)
            {
                boardUI.SetPerspective(true);
            }
            else
            {
                boardUI.SetPerspective(PlayerWhite.Bot is MainBot);
            }
        }

        ChessPlayer CreatePlayer(PlayerType type)
        {
            return type switch
            {
                PlayerType.MyBot => new ChessPlayer(new MainBot(), type, GameDurationMilliseconds),
                PlayerType.EvilBot => new ChessPlayer(new TestBot(), type, GameDurationMilliseconds),
                _ => new ChessPlayer(new HumanPlayer(boardUI), type)
            };
        }

        static (int totalTokenCount, int debugTokenCount) GetTokenCount()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "src", "Main Bot", "MainBot.cs");

            using StreamReader reader = new(path);
            string txt = reader.ReadToEnd();
            return TokenCounter.CountTokens(txt);
        }

        void OnMoveChosen(Move chosenMove)
        {
            if (IsLegal(chosenMove))
            {
                PlayerToMove.AddIncrement(IncrementMilliseconds);
                if (PlayerToMove.IsBot)
                {
                    moveToPlay = chosenMove;
                    isWaitingToPlayMove = true;
                    playMoveTime = lastMoveMadeTime + MinMoveDelay;
                }
                else
                {
                    PlayMove(chosenMove);
                }
            }
            else
            {
                string moveName = MoveUtility.GetMoveNameUCI(chosenMove);
                string log = $"Illegal move: {moveName} in position: {FenUtility.CurrentFen(board)}";
                Log(log, true, ConsoleColor.Red);
                GameResult result = PlayerToMove == PlayerWhite ? GameResult.WhiteIllegalMove : GameResult.BlackIllegalMove;
                EndGame(result);
            }
        }

        void PlayMove(Move move)
        {
            if (isPlaying)
            {
                bool animate = PlayerToMove.IsBot;
                lastMoveMadeTime = (float)Raylib.GetTime();

                // Check if this move is a capture before making the move
                bool isCapture = move.IsEnPassant || board.Square[move.TargetSquareIndex] != PieceHelper.None;

                board.MakeMove(move, false);
                boardUI.UpdatePosition(board, move, animate);

                // Play appropriate sound effect
                if (isCapture)
                {
                    AudioManager.Instance.PlayCaptureSound();
                }
                else
                {
                    AudioManager.Instance.PlayMoveSound();
                }

                GameResult result = Arbiter.GetGameState(board);
                if (result == GameResult.InProgress)
                {
                    NotifyTurnToMove();
                }
                else
                {
                    EndGame(result);
                }
            }
        }

        void EndGame(GameResult result, bool log = true, bool autoStartNextBotMatch = true)
        {
            if (isPlaying)
            {
                isPlaying = false;
                isWaitingToPlayMove = false;
                gameID = -1;

                if (log)
                {
                    Log("Game Over: " + result, false, ConsoleColor.Blue);
                }

                string pgn = PGNCreator.CreatePGN(board, result, GetPlayerName(PlayerWhite), GetPlayerName(PlayerBlack));
                pgns.AppendLine(pgn);

                // Save game to backend
                if (!PlayerWhite.IsBot || !PlayerBlack.IsBot)
                {
                    Log($"🔄 Attempting to save game to backend...", false, ConsoleColor.Cyan);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (gameStorage != null)
                            {
                                Log($"📤 Calling SaveGameAsync...", false, ConsoleColor.Yellow);
                                await gameStorage.SaveGameAsync(board, result, GetPlayerName(PlayerWhite), GetPlayerName(PlayerBlack));
                                Log($"✅ SaveGameAsync completed", false, ConsoleColor.Green);
                            }
                            else
                            {
                                Log("❌ gameStorage is null", false, ConsoleColor.Red);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"❌ Error saving game: {ex.Message}", false, ConsoleColor.Red);
                        }
                    });
                }

                // If 2 bots playing each other, start next game automatically.
                if (PlayerWhite.IsBot && PlayerBlack.IsBot)
                {
                    UpdateBotMatchStats(result);
                    botMatchGameIndex++;
                    int numGamesToPlay = botMatchStartFens.Length * 2;

                    if (botMatchGameIndex < numGamesToPlay && autoStartNextBotMatch)
                    {
                        botAPlaysWhite = !botAPlaysWhite;
                        const int startNextGameDelayMs = 600;
                        System.Timers.Timer autoNextTimer = new(startNextGameDelayMs);
                        int originalGameID = gameID;
                        autoNextTimer.Elapsed += (s, e) => AutoStartNextBotMatchGame(originalGameID, autoNextTimer);
                        autoNextTimer.AutoReset = false;
                        autoNextTimer.Start();

                    }
                    else if (autoStartNextBotMatch)
                    {
                        Log("Match finished", false, ConsoleColor.Blue);
                    }
                }
            }
        }

        private void AutoStartNextBotMatchGame(int originalGameID, System.Timers.Timer timer)
        {
            if (originalGameID == gameID)
            {
                StartNewGame(PlayerBlack.PlayerType, PlayerWhite.PlayerType);
            }
            timer.Close();
        }


        void UpdateBotMatchStats(GameResult result)
        {
            UpdateStats(BotStatsA, botAPlaysWhite);
            UpdateStats(BotStatsB, !botAPlaysWhite);

            void UpdateStats(BotMatchStats stats, bool isWhiteStats)
            {
                // Draw
                if (Arbiter.IsDrawResult(result))
                {
                    stats.NumDraws++;
                }
                // Win
                else if (Arbiter.IsWhiteWinsResult(result) == isWhiteStats)
                {
                    stats.NumWins++;
                }
                // Loss
                else
                {
                    stats.NumLosses++;
                    stats.NumTimeouts += (result is GameResult.WhiteTimeout or GameResult.BlackTimeout) ? 1 : 0;
                    stats.NumIllegalMoves += (result is GameResult.WhiteIllegalMove or GameResult.BlackIllegalMove) ? 1 : 0;
                }
            }
        }

        public void Update()
        {
            if (isPlaying)
            {
                PlayerWhite.Update();
                PlayerBlack.Update();

                PlayerToMove.UpdateClock(Raylib.GetFrameTime());
                if (PlayerToMove.TimeRemainingMs <= 0)
                {
                    EndGame(PlayerToMove == PlayerWhite ? GameResult.WhiteTimeout : GameResult.BlackTimeout);
                }
                else
                {
                    if (isWaitingToPlayMove && Raylib.GetTime() > playMoveTime)
                    {
                        isWaitingToPlayMove = false;
                        PlayMove(moveToPlay);
                    }
                }
            }

            // Update chat UI
            chatUI.Update();

            if (hasBotTaskException)
            {
                hasBotTaskException = false;
                botExInfo.Throw();
            }
        }

        public void Draw()
        {
            boardUI.Draw();
            string nameW = GetPlayerName(PlayerWhite);
            string nameB = GetPlayerName(PlayerBlack);
            boardUI.DrawPlayerNames(nameW, nameB, PlayerWhite.TimeRemainingMs, PlayerBlack.TimeRemainingMs, isPlaying);
        }

        public void DrawOverlay()
        {
            MenuUI.DrawButtons(this);
            chatUI.Draw();
        }

        static string GetPlayerName(ChessPlayer player) => GetPlayerName(player.PlayerType);
        static string GetPlayerName(PlayerType type) => type.ToString();

        public void StartNewBotMatch(PlayerType botTypeA, PlayerType botTypeB)
        {
            EndGame(GameResult.DrawByArbiter, log: false, autoStartNextBotMatch: false);
            botMatchGameIndex = 0;
            string nameA = GetPlayerName(botTypeA);
            string nameB = GetPlayerName(botTypeB);
            if (nameA == nameB)
            {
                nameA += " (A)";
                nameB += " (B)";
            }
            BotStatsA = new BotMatchStats(nameA);
            BotStatsB = new BotMatchStats(nameB);
            botAPlaysWhite = true;
            Log($"Starting new match: {nameA} vs {nameB}", false, ConsoleColor.Blue);
            StartNewGame(botTypeA, botTypeB);
        }


        ChessPlayer PlayerToMove => board.IsWhiteToMove ? PlayerWhite : PlayerBlack;
        ChessPlayer PlayerNotOnMove => board.IsWhiteToMove ? PlayerBlack : PlayerWhite;

        public int TotalGameCount => botMatchStartFens.Length * 2;
        public int CurrGameNumber => Math.Min(TotalGameCount, botMatchGameIndex + 1);
        public string AllPGNs => pgns.ToString();


        bool IsLegal(Move givenMove)
        {
            var moves = moveGenerator.GenerateMoves(board);
            foreach (var legalMove in moves)
            {
                if (givenMove.Value == legalMove.Value)
                {
                    return true;
                }
            }

            return false;
        }

        public class BotMatchStats
        {
            public string BotName;
            public int NumWins;
            public int NumLosses;
            public int NumDraws;
            public int NumTimeouts;
            public int NumIllegalMoves;

            public BotMatchStats(string name) => BotName = name;
        }

        public ChessPositionContext? GetCurrentPositionContext()
        {
            if (!isPlaying || board == null)
            {
                return null;
            }

            try
            {
                // Determine player color
                bool humanIsWhite = PlayerWhite?.IsHuman ?? false;
                bool humanIsBlack = PlayerBlack?.IsHuman ?? false;

                if (!humanIsWhite && !humanIsBlack)
                {
                    // No human player
                    return null;
                }

                string playerColor = humanIsWhite ? "White" : "Black";
                int playerColorIndex = humanIsWhite ? Board.WhiteIndex : Board.BlackIndex;
                int opponentColorIndex = humanIsWhite ? Board.BlackIndex : Board.WhiteIndex;

                // Calculate move number
                int moveNumber = (board.plyCount / 2) + 1;

                // Get turn to move
                string turnToMove = board.IsWhiteToMove ? "White" : "Black";

                // Count material
                var (playerMaterial, playerPieces) = CountMaterial(playerColorIndex);
                var (opponentMaterial, opponentPieces) = CountMaterial(opponentColorIndex);

                // Get recent moves (last 5)
                string[] recentMoves = GetRecentMoves(5);

                // Determine game phase
                string gamePhase = DetermineGamePhase(moveNumber);

                // Check if in check
                bool isInCheck = board.IsInCheck();

                // Count legal moves (only if < 5)
                var legalMoves = moveGenerator.GenerateMoves(board);
                int? legalMoveCount = legalMoves.Length < 5 ? legalMoves.Length : null;

                return new ChessPositionContext
                {
                    PlayerColor = playerColor,
                    MoveNumber = moveNumber,
                    TurnToMove = turnToMove,
                    PlayerMaterialCentipawns = playerMaterial,
                    OpponentMaterialCentipawns = opponentMaterial,
                    PlayerPieces = playerPieces,
                    OpponentPieces = opponentPieces,
                    RecentMoves = recentMoves,
                    GamePhase = gamePhase,
                    IsInCheck = isInCheck,
                    LegalMoveCount = legalMoveCount
                };
            }
            catch (Exception ex)
            {
                Log($"Error creating position context: {ex.Message}", false, ConsoleColor.Red);
                Log($"Stack trace: {ex.StackTrace}", false, ConsoleColor.Red);
                return null;
            }
        }

        private (int material, string[] pieces) CountMaterial(int colorIndex)
        {
            int material = 0;
            var piecesList = new System.Collections.Generic.List<string>();

            // Piece values from Evaluation
            int[] pieceValues = { 0, 100, 320, 330, 500, 900, 0 }; // Pawn, Knight, Bishop, Rook, Queen, King

            // Null checks for piece lists
            if (board.pawns.Length > colorIndex)
            {
                int pawnCount = board.pawns[colorIndex].Count;
                material += pawnCount * pieceValues[PieceHelper.Pawn];
                if (pawnCount > 0) piecesList.Add($"{pawnCount}P");
            }

            if (board.knights.Length > colorIndex)
            {
                int knightCount = board.knights[colorIndex].Count;
                material += knightCount * pieceValues[PieceHelper.Knight];
                if (knightCount > 0) piecesList.Add($"{knightCount}N");
            }

            if (board.bishops.Length > colorIndex)
            {
                int bishopCount = board.bishops[colorIndex].Count;
                material += bishopCount * pieceValues[PieceHelper.Bishop];
                if (bishopCount > 0) piecesList.Add($"{bishopCount}B");
            }

            if (board.rooks.Length > colorIndex)
            {
                int rookCount = board.rooks[colorIndex].Count;
                material += rookCount * pieceValues[PieceHelper.Rook];
                if (rookCount > 0) piecesList.Add($"{rookCount}R");
            }

            if (board.queens.Length > colorIndex)
            {
                int queenCount = board.queens[colorIndex].Count;
                material += queenCount * pieceValues[PieceHelper.Queen];
                if (queenCount > 0) piecesList.Add($"{queenCount}Q");
            }

            return (material, piecesList.ToArray());
        }

        private string[] GetRecentMoves(int count)
        {
            if (board.AllGameMoves == null || board.AllGameMoves.Count == 0)
            {
                return Array.Empty<string>();
            }

            try
            {
                var recentMoves = new System.Collections.Generic.List<string>();
                int startIndex = Math.Max(0, board.AllGameMoves.Count - count);

                for (int i = startIndex; i < board.AllGameMoves.Count; i++)
                {
                    Move move = board.AllGameMoves[i];
                    string moveUci = MoveUtility.GetMoveNameUCI(move);
                    recentMoves.Add(moveUci);
                }

                return recentMoves.ToArray();
            }
            catch (Exception ex)
            {
                Log($"Error getting recent moves: {ex.Message}", false, ConsoleColor.Yellow);
                return Array.Empty<string>();
            }
        }

        private string DetermineGamePhase(int moveNumber)
        {
            if (moveNumber < 10) return "Opening";
            if (moveNumber > 40) return "Endgame";
            return "Middlegame";
        }

        public void Release()
        {
            boardUI.Release();
        }
    }
}
