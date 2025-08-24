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
    
    // Set timeLimit to 0 to use match Timer in GUI mode
    int timeLimit = 200; // ms
    bool useTimer;
    bool useMoveTime;
    bool guiModeIsTimerConfiged = false;
    int uciModeTimeReducion = 25;
    bool searchCanceled;
    
    int positionsEvaluated;
    
    public Move Think(Board board, Timer timer)
    {
        if (!ChessChallenge.Application.Program.UCI_MODE && !guiModeIsTimerConfiged)
        {
            if (timeLimit == 0)
            {
                SetTimerMode();
            }
            else
            {
                SetMoveTimeMode(timeLimit);
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
        searchCanceled = false;
        
        if (transpositionTable == null)
            transpositionTable = new TranspositionTable(64);
            
        if (moveOrdering == null)
            moveOrdering = new MoveOrdering();
        
        // Set time limit based on timer mode
        if (useMoveTime) {
            // timeLimit is already set correctly for movetime mode
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
            timeLimit = (int) Math.Ceiling(Math.Max(minThinkTime, thinkTimeMs));
            
            // To make sure we don't go past the allowed time
            timeLimit -= (ChessChallenge.Application.Program.UCI_MODE ? uciModeTimeReducion : 0);
        }
        
        searchStopwatch = Stopwatch.StartNew();
        
        int bestEval = 0;
        int depthSearched = 0;
        
        // Iterative deepening loop
        for (int depth = 1; depth <= 256; depth++)
        {
            int eval = Search(board, depth, -9999999, 9999999, true);
            depthSearched++;
            
            // Search fully completed
            if (!searchCanceled)
            {
                bestMove = bestMoveThisIteration;
                bestEval = eval;
                
                bestMoveThisIteration = Move.NullMove;
                
                // Stop if we found a winning checkmate for us
                if (eval > 29000)  // We're delivering mate
                {
                    break;
                }
            }
            else
            // Search cancelled mid-search
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
        timeLimit = moveTimeMs - (ChessChallenge.Application.Program.UCI_MODE ? uciModeTimeReducion : 0); // To make sure we don't go past the allowed time
    }
    
    public void SetTimerMode()
    {
        useMoveTime = false;
        useTimer = true;
    }
    
    int Search(Board board, int depth, int alpha, int beta, bool isRoot)
    {
        // Check time limit periodically (not every node to avoid overhead)
        if (positionsEvaluated % 1000 == 0 && searchStopwatch.ElapsedMilliseconds > timeLimit)
        {
            searchCanceled = true;
            return 0; // Return immediately if time is up
        }
        
        if (searchCanceled)
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
        
        Move bestMoveThisPosition = movesSpan.Length > 0 ? movesSpan[0] : Move.NullMove;
        int bestValue = int.MinValue;
        
        foreach (Move move in movesSpan)
        {
            if (searchCanceled)
                break;
                
            board.MakeMove(move);
            int value = -Search(board, depth - 1, -beta, -alpha, false);
            board.UndoMove(move);
            
            if (searchCanceled)
                break;
            
            if (value > bestValue)
            {
                bestValue = value;
                bestMoveThisPosition = move;
            }
            
            alpha = Math.Max(alpha, value);
            if (beta <= alpha)
                break;
        }
        
        // Only store in TT if search wasn't interrupted
        if (!searchCanceled)
        {
            NodeType nodeType = bestValue <= originalAlpha ? NodeType.UpperBound :
                               bestValue >= beta ? NodeType.LowerBound : NodeType.Exact;
            
            transpositionTable.StoreEntry(zobristKey, bestValue, depth, nodeType, bestMoveThisPosition);
            
            if (isRoot)
                bestMoveThisIteration = bestMoveThisPosition;
        }
            
        return bestValue;
    }
    
    int QuiescenceSearch(Board board, int alpha, int beta)
    {
        // Check if search was canceled (time ran out)
        if (searchCanceled)
            return 0;  // Or return current evaluation
    
        // Periodic time check (less frequent than main search since qsearch is faster)
        if (positionsEvaluated % 1500 == 0 && searchStopwatch.ElapsedMilliseconds > timeLimit)
        {
            searchCanceled = true;
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
            if (searchCanceled)
                return evaluation;  // Return static eval when canceled
            
            board.MakeMove(capture);
            int value = -QuiescenceSearch(board, -beta, -alpha);
            board.UndoMove(capture);
            
            // Check cancellation after recursive call
            if (searchCanceled)
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