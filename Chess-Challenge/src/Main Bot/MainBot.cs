using ChessChallenge.API;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Text;

public class MainBot : IChessBot
{
    Move bestMoveThisIteration;
    Move bestMove; // Best move so far
    
    TranspositionTable? transpositionTable;
    MoveOrdering? moveOrdering;
    RepetitionTable? repetitionTable;
    
    OpeningBook openingBook = new(LoadBookData());
    
    Stopwatch searchStopwatch;

    const int maxExtensions = 13;
    
    // Set hardTimeLimit to 0 to use match Timer in GUI mode
    int hardTimeLimit = 200; // ms
    int softTimeLimit;
    bool useTimer;
    bool useMoveTime;
    bool guiModeIsTimerConfiged = false;
    int uciModeTimeReducion = 25;
    bool searchCanceledAfterIteration;
    bool searchCanceledImmediately;
    
    int positionsEvaluated;
    
    // Search logging variables
    string logFilePath;
    List<Move> currentPV = new List<Move>();
    int currentBestEval;
    bool isSearchCanceled;
    bool isLogFileInitialized = false;
    Dictionary<int, List<Move>> pvTable = new Dictionary<int, List<Move>>();
    
    public Move Think(Board board, Timer timer)
    {
        // Initialize logging
        InitializeLogging(board);
        
        if (!ChessChallenge.Application.Program.UCI_MODE && !guiModeIsTimerConfiged)
        {
            if (hardTimeLimit == 0)
            {
                SetTimerMode();
            }
            else
            {
                SetMoveTimeMode(hardTimeLimit);
            }
            guiModeIsTimerConfiged = true;
        }
        
        // Book moves
        if (board.PlyCount <= 18 && openingBook.TryGetBookMove(board, out string moveString, 0.8)) {
            LogBookMove(moveString);
            if(!ChessChallenge.Application.Program.UCI_MODE) Console.WriteLine($"Book move found: {moveString}");
            return new Move(moveString, board);
        }
        
        positionsEvaluated = 0;
        bestMoveThisIteration = Move.NullMove;
        bestMove = Move.NullMove;
        searchCanceledImmediately = false;
        searchCanceledAfterIteration = false;
        isSearchCanceled = false;
        
        if (transpositionTable == null)
            transpositionTable = new TranspositionTable(64);
            
        if (moveOrdering == null)
            moveOrdering = new MoveOrdering();

        if (repetitionTable == null)
            repetitionTable = new RepetitionTable();
        
        moveOrdering.ResetKillers();
        moveOrdering.ResetHistory();
        
        repetitionTable.Init(board);
        
        // Set time limit based on timer mode
        if (useMoveTime) {
            // hardTimeLimit is already set correctly for movetime mode
            
            softTimeLimit = hardTimeLimit;
        } else if (useTimer) {
            int myTimeRemainingMs = timer.MillisecondsRemaining;
            int myIncrementMs = timer.IncrementMilliseconds;
            
            // Get a fraction of remaining time to use for current move
            double thinkTimeMs = myTimeRemainingMs / 40.0;
            // Clamp think time
            thinkTimeMs = Math.Min(5000, thinkTimeMs);
            // Add increment
            if (myTimeRemainingMs > myIncrementMs * 2) {
                thinkTimeMs += myIncrementMs * 0.8;
            }

            double minThinkTime = Math.Min(50, myTimeRemainingMs * 0.25);
            softTimeLimit = (int) Math.Ceiling(Math.Max(minThinkTime, thinkTimeMs));
            
            // To make sure we don't go past the allowed time
            softTimeLimit -= (ChessChallenge.Application.Program.UCI_MODE ? uciModeTimeReducion : 0);

            // Allow the bot to go a bit past the soft limit to finish the iteration
            // Base extension: 30% of soft limit, but scale down under time pressure
            double extensionPercent;
            if (myTimeRemainingMs < 5000) {
                extensionPercent = 0.1;  // 10% when very low on time
            } else if (myTimeRemainingMs < 30000) {
                extensionPercent = 0.2;  // 20% when moderately low
            } else {
                extensionPercent = 0.3;  // 30% when comfortable on time
            }
    
            int extension = (int)(softTimeLimit * extensionPercent);
    
            // Cap extension at 500ms max and ensure minimum 25ms
            extension = Math.Clamp(extension, 25, 500);
    
            // Make sure we don't exceed remaining time minus safety buffer
            int safetyBuffer = Math.Max(50, myTimeRemainingMs / 20);
            int maxAllowedTime = myTimeRemainingMs - safetyBuffer;
    
            hardTimeLimit = Math.Min(softTimeLimit + extension, maxAllowedTime);
    
            // Ensure hard limit is at least slightly higher than soft
            hardTimeLimit = Math.Max(hardTimeLimit, softTimeLimit + 10);
        }
        
        LogSearchStart(board);
        
        searchStopwatch = Stopwatch.StartNew();
        
        int bestEval = 0;
        int depthSearched = 0;
        
        // Iterative deepening loop
        for (int depth = 1; depth <= 256; depth++)
        {
            // Don't start a new iteration if the soft limit is hit
            if (searchStopwatch.ElapsedMilliseconds > softTimeLimit)
            {
                break;
            }
            
            LogIterationStart(depth);
            
            currentPV.Clear();
            pvTable.Clear();
            
            int eval = Search(board, depth, -9999999, 9999999, true);
            
            depthSearched++;
            
            // Iteration fully completed
            if (!searchCanceledImmediately)
            {
                bestMove = bestMoveThisIteration;
                bestEval = eval;
                
                LogIterationResult(depth, bestMoveThisIteration, eval, false);
                
                bestMoveThisIteration = Move.NullMove;
                
                // Stop if we found a winning checkmate for us
                if (eval > 29000)  // We're delivering mate
                {
                    LogMateFound(eval, depth);
                    break;
                }
                
                // Stop if we hit soft time limit (iteration completed)
                if (searchCanceledAfterIteration)
                {
                    LogSearchCanceled("soft time limit reached");
                    break;
                }
            }
            else
            // Search cancelled mid-search (hard limit hit)
            {
                if (bestMoveThisIteration != Move.NullMove)
                {
                    // Use the best move we've found so far in this iteration
                    // This will be fine if we've at least searched one move
                    // Since our move ordering will always put the best move found so far first
                    bestMove = bestMoveThisIteration;
                    bestEval = eval;
                    LogIterationResult(depth, bestMoveThisIteration, eval, true);
                }
                else
                {
                    LogSearchCanceled("hard time limit reached during iteration " + depth);
                }
                break;
            }
        }
        
        searchStopwatch.Stop();
        
        LogSearchComplete(depthSearched, bestMove, bestEval);
        
        if(!ChessChallenge.Application.Program.UCI_MODE)
            Console.WriteLine($"Depth searched: {depthSearched}, Final move: {bestMove.ToString()}, Time took: {searchStopwatch.ElapsedMilliseconds}ms, " +
                        $"Positions evaluated: {positionsEvaluated}, Score: {bestEval}");

        if (bestMove == Move.NullMove)
        {
            bestMove = board.GetLegalMoves()[0];
            LogFallbackMove(bestMove);
            if(!ChessChallenge.Application.Program.UCI_MODE) Console.WriteLine("Best move not found, using first legal move");
        }

        return bestMove;
    }
    
    public void SetMoveTimeMode(int moveTimeMs)
    {
        useMoveTime = true;
        useTimer = false;
        hardTimeLimit = moveTimeMs - (ChessChallenge.Application.Program.UCI_MODE ? uciModeTimeReducion : 0); // To make sure we don't go past the allowed time
    }
    
    public void SetTimerMode()
    {
        useMoveTime = false;
        useTimer = true;
    }
    
    int Search(Board board, int depth, int alpha, int beta, bool isRoot, int numExtensions = 0, int ply = 0, Move prevMove = default)
    {
        // Initialize PV for this ply
        if (!pvTable.ContainsKey(ply))
            pvTable[ply] = new List<Move>();
        pvTable[ply].Clear();
        
        // Check time limit periodically (not every node to avoid overhead)
        if (positionsEvaluated % 1000 == 0)
        {
            // Hit hard limit
            if (searchStopwatch.ElapsedMilliseconds > hardTimeLimit)
            {
                searchCanceledImmediately = true;
                isSearchCanceled = true;
                return 0;
            }
            // Hit soft limit
            if (useTimer && searchStopwatch.ElapsedMilliseconds > softTimeLimit)
            {
                searchCanceledAfterIteration = true;
                // Don't return - let iteration complete
            }
        }
        
        if (searchCanceledImmediately)
            return 0;
        
        ulong zobristKey = board.ZobristKey;
        int originalAlpha = alpha;
        
        if (transpositionTable.ProbeEntry(zobristKey, depth, alpha, beta, out int ttValue, out Move ttMove))
        {
            if (!isRoot && Math.Abs(ttValue) < 29000)
                return ttValue;
        }
        
        if (board.IsInCheckmate())
        {
            positionsEvaluated++;
            // Current player is checkmated - this is bad for them
            return -30000 + depth;  // Add depth to prefer shorter mates
        }
        
        // Check repetition table
        // (Note: returns a draw score even if this position has only appeared once for sake of simplicity)
        if (repetitionTable.Contains(zobristKey))
        {
            return 0;
        }
        
        if (board.IsFiftyMoveDraw() || board.IsInsufficientMaterial() || board.IsInStalemate())
        {
            positionsEvaluated++;
            return 0;
        }
        
        if (depth == 0)
        {
            return QuiescenceSearch(board, alpha, beta);
        }
        
        Span<Move> movesSpan = stackalloc Move[218];
        board.GetLegalMovesNonAlloc(ref movesSpan);
        moveOrdering.OrderMoves(board, ref movesSpan, false, ply, ttMove);
        
        if (ply > 0)
        {
            // Record position in repetition table
            bool shouldReset = prevMove.IsCapture || prevMove.MovePieceType == PieceType.Pawn;
            repetitionTable.Push(board.ZobristKey, shouldReset);
        }
        
        Move bestMoveThisPosition = Move.NullMove;
        int bestScore = int.MinValue;
        
        foreach (Move move in movesSpan)
        {
            if (searchCanceledImmediately)
                break;
                
            board.MakeMove(move);
            
            // Extend the depth of the search in certain interesting cases
            int extension = 0;
            if (numExtensions < maxExtensions)
            {
                if (board.IsInCheck())
                {
                    extension = 1;
                }
                else if (move.MovePieceType == PieceType.Pawn && (move.TargetSquare.Rank == 6 || move.TargetSquare.Rank == 1))
                {
                    extension = 1;
                }
            }
            
            int score = -Search(board, depth - 1 + extension, -beta, -alpha, false, numExtensions + extension, ply + 1, move);
            board.UndoMove(move);
            
            if (searchCanceledImmediately)
                break;
            
            if (score > bestScore)
            {
                bestScore = score;
                bestMoveThisPosition = move;
                
                // Update PV - copy the line from the child node and prepend this move
                pvTable[ply].Clear();
                pvTable[ply].Add(move);
                
                // Copy child PV if it exists
                if (pvTable.ContainsKey(ply + 1))
                {
                    pvTable[ply].AddRange(pvTable[ply + 1]);
                }
                
                // Update root PV
                if (isRoot)
                {
                    currentBestEval = score;
                    currentPV.Clear();
                    currentPV.AddRange(pvTable[ply]);
                }
            }
            
            if (score >= beta) // fail-high cutoff
            {
                if (move.CapturePieceType == PieceType.None && !move.IsPromotion)
                {
                    // Record as killer move
                    if (!isRoot)
                        moveOrdering.AddKiller(ply, move);
                
                    // Update history heuristic
                    int historyScore = depth * depth;
                    int colorIndex = board.IsWhiteToMove ? 0 : 1;
                    moveOrdering.history[colorIndex, move.StartSquare.Index, move.TargetSquare.Index] += historyScore;
                }

                // Store as LOWER bound at this node
                transpositionTable.StoreEntry(zobristKey, beta, depth, NodeType.LowerBound, bestMoveThisPosition);
                
                if (ply > 0)
                {
                    repetitionTable.TryPop();
                }
                
                return beta; // fail-hard
            }

            if (score > alpha)
            {
                alpha = score;
            }
        }
        
        // Only store in TT if search wasn't interrupted
        if (!searchCanceledImmediately)
        {
            NodeType nodeType = bestScore <= originalAlpha ? NodeType.UpperBound :
                               bestScore >= beta ? NodeType.LowerBound : NodeType.Exact;
            
            transpositionTable.StoreEntry(zobristKey, bestScore, depth, nodeType, bestMoveThisPosition);
            
            if (isRoot)
                bestMoveThisIteration = bestMoveThisPosition;
        }
        
        if (ply > 0)
        {
            repetitionTable.TryPop();
        }
            
        return bestScore;
    }
    
    int QuiescenceSearch(Board board, int alpha, int beta)
    {
        // Check if search was canceled (time ran out)
        if (searchCanceledImmediately)
            return 0;  // Or return current evaluation
    
        // Periodic time check (less frequent than main search since qsearch is faster)
        if (positionsEvaluated % 1500 == 0 && searchStopwatch.ElapsedMilliseconds > hardTimeLimit)
        {
            searchCanceledImmediately = true;
            return 0;
        }
        
        if (board.IsInCheckmate())
        {
            // Current player is checkmated - return negative
            return -30000;
        }
        
        if (board.IsDraw())
        {
            return 0;
        }
        
        positionsEvaluated++;
        int evaluation = Evaluation.Evaluate(board);
        
        if (evaluation >= beta)
            return beta;
        
        alpha = Math.Max(alpha, evaluation);
        
        Span<Move> capturesSpan = stackalloc Move[218];
        board.GetLegalMovesNonAlloc(ref capturesSpan, true);
        moveOrdering.OrderMoves(board, ref capturesSpan, true);
        
        foreach (Move capture in capturesSpan)
        {
            // Check cancellation before making a move
            if (searchCanceledImmediately)
                return evaluation;  // Return static eval when canceled
            
            board.MakeMove(capture);
            int value = -QuiescenceSearch(board, -beta, -alpha);
            board.UndoMove(capture);
            
            // Check cancellation after recursive call
            if (searchCanceledImmediately)
                return evaluation;  // Return static eval when canceled
            
            if (value >= beta)
                return beta;
            
            alpha = Math.Max(alpha, value);
        }
        
        return alpha;
    }
    
    // Logging methods
    private void InitializeLogging(Board board)
    {
        try
        {
            if (!isLogFileInitialized)
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                logFilePath = Path.Combine(@"E:\Coding\ChessBot\Search Logs", $"game_log_{timestamp}.txt");
                
                using (StreamWriter writer = new StreamWriter(logFilePath, false))
                {
                    writer.WriteLine($"=== Chess Engine Game Log ===");
                    writer.WriteLine($"Game started: {DateTime.Now}");
                    writer.WriteLine();
                }
                isLogFileInitialized = true;
            }
            
            // Log this specific search start
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"--- Search #{board.PlyCount / 2 + 1} ---");
                writer.WriteLine($"Position FEN: {board.GetFenString()}");
                writer.WriteLine($"Move to play: {(board.IsWhiteToMove ? "White" : "Black")}");
                writer.WriteLine($"Ply count: {board.PlyCount}");
            }
        }
        catch
        {
            // If we can't create the log file, just continue without logging
            logFilePath = null;
        }
    }
    
    private void LogBookMove(string moveString)
    {
        if (logFilePath == null) return;
        try
        {
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"Book move found: {moveString}");
                writer.WriteLine();
            }
        }
        catch { }
    }
    
    private void LogSearchStart(Board board)
    {
        if (logFilePath == null) return;
        try
        {
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"Starting search with FEN: {board.GetFenString()}");
                writer.WriteLine($"Time limits - Soft: {softTimeLimit}ms, Hard: {hardTimeLimit}ms");
                writer.WriteLine();
            }
        }
        catch { }
    }
    
    private void LogIterationStart(int depth)
    {
        if (logFilePath == null) return;
        try
        {
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"Starting Iteration: {depth}");
            }
        }
        catch { }
    }
    
    private void LogIterationResult(int depth, Move bestMove, int eval, bool wasCanceled)
    {
        if (logFilePath == null) return;
        try
        {
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                string status = wasCanceled ? " (partial result)" : "";
                writer.WriteLine($"Iteration result: {bestMove} Eval: {eval}{status}");
                
                // Build PV string
                StringBuilder pvBuilder = new StringBuilder();
                if (currentPV.Count > 0)
                {
                    for (int i = 0; i < currentPV.Count; i++)
                    {
                        if (i > 0) pvBuilder.Append(" ");
                        pvBuilder.Append(currentPV[i].ToString());
                    }
                }
                else if (bestMove != Move.NullMove)
                {
                    pvBuilder.Append(bestMove.ToString());
                }
                
                writer.WriteLine($"Iteration pv: PV (depth = {depth}): {pvBuilder}");
                writer.WriteLine($"Time: {searchStopwatch.ElapsedMilliseconds}ms, Nodes: {positionsEvaluated}");
                writer.WriteLine();
            }
        }
        catch { }
    }
    
    private void LogMateFound(int eval, int depth)
    {
        if (logFilePath == null) return;
        try
        {
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                int mateInMoves = (30000 - eval + depth) / 2;
                writer.WriteLine($"Mate found! Mate in {mateInMoves} moves (eval: {eval})");
            }
        }
        catch { }
    }
    
    private void LogSearchCanceled(string reason)
    {
        if (logFilePath == null) return;
        try
        {
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"Search cancelled: {reason}");
                if (bestMove != Move.NullMove)
                {
                    writer.WriteLine($"Using partial search result: {bestMove} Eval: {currentBestEval}");
                }
                writer.WriteLine();
            }
        }
        catch { }
    }
    
    private void LogSearchComplete(int depthSearched, Move finalMove, int finalEval)
    {
        if (logFilePath == null) return;
        try
        {
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"=== Search Complete ===");
                writer.WriteLine($"Final depth searched: {depthSearched}");
                writer.WriteLine($"Final move: {finalMove}");
                writer.WriteLine($"Final evaluation: {finalEval}");
                writer.WriteLine($"Total time: {searchStopwatch.ElapsedMilliseconds}ms");
                writer.WriteLine($"Total positions evaluated: {positionsEvaluated}");
                writer.WriteLine($"Nodes per second: {(positionsEvaluated * 1000.0 / Math.Max(1, searchStopwatch.ElapsedMilliseconds)):F0}");
                writer.WriteLine($"Completed at: {DateTime.Now}");
                writer.WriteLine("=====================================\n");
                writer.WriteLine();
            }
        }
        catch { }
    }
    
    private void LogFallbackMove(Move fallbackMove)
    {
        if (logFilePath == null) return;
        try
        {
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"WARNING: No best move found, using fallback: {fallbackMove}");
            }
        }
        catch { }
    }
    
    private static string LoadBookData()
    {
        // Try external file first (for development/customization)
        try
        {
            string filePath = Path.Combine(ChessChallenge.Application.FileHelper.GetResourcePath(), "Book.txt");
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
        }
        catch
        {
            // Fall through to embedded resource
        }
        
        // Fall back to embedded resource
        return LoadEmbeddedBookData();
    }
    
    private static string LoadEmbeddedBookData()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Chess_Challenge.resources.Book.txt");
        if (stream == null)
        {
            throw new FileNotFoundException("Book.txt not found as embedded resource or external file");
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}