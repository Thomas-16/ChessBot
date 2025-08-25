using ChessChallenge.API;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

public class MainBot : IChessBot
{
    Move bestMoveThisIteration;
    Move bestMove; // Best move so far
    
    TranspositionTable? transpositionTable;
    MoveOrdering? moveOrdering;
    
    OpeningBook openingBook = new(LoadBookData());
    
    Stopwatch searchStopwatch;

    const int maxExtensions = 16;
    
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
    
    public Move Think(Board board, Timer timer)
    {
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
            if(!ChessChallenge.Application.Program.UCI_MODE) Console.WriteLine($"Book move found: {moveString}");
            return new Move(moveString, board);
        }
        
        positionsEvaluated = 0;
        bestMoveThisIteration = Move.NullMove;
        bestMove = Move.NullMove;
        searchCanceledImmediately = false;
        searchCanceledAfterIteration = false;
        
        if (transpositionTable == null)
            transpositionTable = new TranspositionTable(64);
            
        if (moveOrdering == null)
            moveOrdering = new MoveOrdering();
        
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
            
            int eval = Search(board, depth, -9999999, 9999999, true);
            depthSearched++;
            
            // Iteration fully completed
            if (!searchCanceledImmediately)
            {
                bestMove = bestMoveThisIteration;
                bestEval = eval;
                
                bestMoveThisIteration = Move.NullMove;
                
                // Stop if we found a winning checkmate for us
                if (eval > 29000)  // We're delivering mate
                {
                    break;
                }
                
                // Stop if we hit soft time limit (iteration completed)
                if (searchCanceledAfterIteration)
                {
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
                }
                break;
            }
        }
        
        searchStopwatch.Stop();
        if(!ChessChallenge.Application.Program.UCI_MODE)
            Console.WriteLine($"Depth searched: {depthSearched}, Final move: {bestMove.ToString()}, Time took: {searchStopwatch.ElapsedMilliseconds}ms, " +
                        $"Positions evaluated: {positionsEvaluated}, Score: {bestEval}");

        if (bestMove == Move.NullMove)
        {
            bestMove = board.GetLegalMoves()[0];
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
    
    int Search(Board board, int depth, int alpha, int beta, bool isRoot, int numExtensions = 0)
    {
        // Check time limit periodically (not every node to avoid overhead)
        if (positionsEvaluated % 1000 == 0)
        {
            // Hit hard limit
            if (searchStopwatch.ElapsedMilliseconds > hardTimeLimit)
            {
                searchCanceledImmediately = true;
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
        
        if (board.IsDraw())
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
        moveOrdering.OrderMoves(board, ref movesSpan, ttMove);
        
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
            }
            
            int score = -Search(board, depth - 1, -beta, -alpha, false, numExtensions + extension);
            board.UndoMove(move);
            
            if (searchCanceledImmediately)
                break;
            
            if (score > bestScore)
            {
                bestScore = score;
                bestMoveThisPosition = move;
            }
            
            alpha = Math.Max(alpha, score);
            if (beta <= alpha)
                break;
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
        moveOrdering.OrderMoves(board, ref capturesSpan);
        
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