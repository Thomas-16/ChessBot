using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

public class UCIEngine
{
    private MainBot bot;
    private Board board;
    private Thread searchThread;
    private bool isSearching;
    private volatile bool shouldStop;
    private CancellationTokenSource cancellationTokenSource;
    private Move bestMove;
    
    public UCIEngine()
    {
        bot = new MainBot();
        board = Board.CreateBoardFromFEN("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
    }
    
    public void Run()
    {
        Console.WriteLine("ChessBot UCI Engine by Thomas Fang");
        
        string input;
        while ((input = Console.ReadLine()) != null)
        {
            ProcessCommand(input.Trim());
        }
    }
    
    private void ProcessCommand(string command)
    {
        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        
        string cmd = parts[0].ToLower();
        
        switch (cmd)
        {
            case "uci":
                HandleUCI();
                break;
                
            case "isready":
                Console.WriteLine("readyok");
                break;
                
            case "ucinewgame":
                HandleNewGame();
                break;
                
            case "position":
                HandlePosition(parts);
                break;
                
            case "go":
                HandleGo(parts);
                break;
                
            case "stop":
                HandleStop();
                break;
                
            case "quit":
                HandleQuit();
                return;
                
            case "setoption":
                // Handle UCI options if needed
                break;
                
            default:
                // Unknown command, ignore
                break;
        }
    }
    
    private void HandleUCI()
    {
        Console.WriteLine("id name ChessBot");
        Console.WriteLine("id author Thomas Fang");
        Console.WriteLine("uciok");
    }
    
    private void HandleNewGame()
    {
        board = Board.CreateBoardFromFEN("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        bot = new MainBot();
    }
    
    private void HandlePosition(string[] parts)
    {
        if (parts.Length < 2) return;
        
        if (parts[1] == "startpos")
        {
            board = Board.CreateBoardFromFEN("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
            
            // Apply moves if any
            int movesIndex = Array.IndexOf(parts, "moves");
            if (movesIndex != -1 && movesIndex + 1 < parts.Length)
            {
                for (int i = movesIndex + 1; i < parts.Length; i++)
                {
                    try
                    {
                        Move move = new Move(parts[i], board);
                        
                        board.MakeMove(move);
                    }
                    catch
                    {
                        // Invalid move, ignore
                    }
                }
            }
        }
        else if (parts[1] == "fen" && parts.Length >= 8)
        {
            // Reconstruct FEN string
            string fen = string.Join(" ", parts, 2, 6);
            try
            {
                board = Board.CreateBoardFromFEN(fen);
                
                // Apply moves if any
                int movesIndex = Array.IndexOf(parts, "moves");
                if (movesIndex != -1 && movesIndex + 1 < parts.Length)
                {
                    for (int i = movesIndex + 1; i < parts.Length; i++)
                    {
                        try
                        {
                            Move move = new Move(parts[i], board);
                            
                            board.MakeMove(move);
                        }
                        catch
                        {
                            // Invalid move, ignore
                        }
                    }
                }
            }
            catch
            {
                // Invalid FEN, ignore
            }
        }
    }
    
    private void HandleGo(string[] parts)
    {
        if (isSearching)
        {
            HandleStop();
        }
        
        // Parse time controls
        int? moveTime = null;
        int? whiteTime = null;
        int? blackTime = null;
        int? whiteIncrement = null;
        int? blackIncrement = null;
        
        for (int i = 1; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLower())
            {
                case "wtime":
                    if (int.TryParse(parts[i + 1], out int wtime))
                    {
                        whiteTime = wtime;
                    }
                    break;
                    
                case "btime":
                    if (int.TryParse(parts[i + 1], out int btime))
                    {
                        blackTime = btime;
                    }
                    break;
                    
                case "winc":
                    if (int.TryParse(parts[i + 1], out int winc))
                    {
                        whiteIncrement = winc;
                    }
                    break;
                    
                case "binc":
                    if (int.TryParse(parts[i + 1], out int binc))
                    {
                        blackIncrement = binc;
                    }
                    break;
                    
                case "movetime":
                    if (int.TryParse(parts[i + 1], out int movetime))
                    {
                        moveTime = movetime;
                    }
                    break;
                    
                case "depth":
                    // Depth limit not implemented in MainBot
                    break;
            }
        }
        
        StartSearch(moveTime, whiteTime, blackTime, whiteIncrement, blackIncrement);
    }
    
    private void StartSearch(int? moveTime, int? whiteTime, int? blackTime, int? whiteIncrement, int? blackIncrement)
    {
        isSearching = true;
        shouldStop = false;
        bestMove = Move.NullMove;
        
        // Create cancellation token for this search
        cancellationTokenSource = new CancellationTokenSource();
        
        searchThread = new Thread(() =>
        {
            try
            {
                ChessChallenge.API.Timer timer;
                
                if (moveTime.HasValue)
                {
                    // Use exact movetime when provided
                    timer = new ChessChallenge.API.Timer(moveTime.Value);
                    bot.SetMoveTimeMode(moveTime.Value);
                }
                else
                {
                    // Use wtime/btime with MainBot's time calculation
                    bool isWhite = board.IsWhiteToMove;
                    int myTime = isWhite ? (whiteTime ?? 5000) : (blackTime ?? 5000);
                    int opponentTime = isWhite ? (blackTime ?? 5000) : (whiteTime ?? 5000);
                    int myIncrement = isWhite ? (whiteIncrement ?? 0) : (blackIncrement ?? 0);
                    
                    timer = new ChessChallenge.API.Timer(myTime, opponentTime, myTime, myIncrement);
                    bot.SetTimerMode();
                }
                
                bestMove = bot.Think(board, timer);
                
                if (!shouldStop && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    Console.WriteLine($"bestmove {bestMove}");
                }
            }
            catch (Exception)
            {
                // If search fails, try to find any legal move
                Move[] legalMoves = board.GetLegalMoves();
                if (legalMoves.Length > 0)
                {
                    bestMove = legalMoves[0];
                    if (!shouldStop && !cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        Console.WriteLine($"bestmove {bestMove}");
                    }
                }
                else
                {
                    Console.WriteLine("bestmove 0000");
                }
            }
            finally
            {
                isSearching = false;
                cancellationTokenSource?.Dispose();
            }
        });
        
        searchThread.Start();
    }
    
    private void HandleStop()
    {
        if (isSearching)
        {
            shouldStop = true;
            
            // Cancel the search using the cancellation token
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
            }
            
            if (searchThread != null && searchThread.IsAlive)
            {
                searchThread.Join(1000); // Wait up to 1 second for graceful shutdown
            }
            
            if (bestMove != Move.NullMove)
            {
                Console.WriteLine($"bestmove {bestMove}");
            }
            else
            {
                Move[] legalMoves = board.GetLegalMoves();
                if (legalMoves.Length > 0)
                {
                    Console.WriteLine($"bestmove {legalMoves[0]}");
                }
                else
                {
                    Console.WriteLine("bestmove 0000");
                }
            }
            
            isSearching = false;
        }
    }
    
    private void HandleQuit()
    {
        HandleStop();
        Environment.Exit(0);
    }
}