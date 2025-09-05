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

    public const int immediateMateScore = 30000;

    const int maxExtensions = 13;
    
    const int maxAspirationReSearches = 4; // Maximum re-searches at same depth
    
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
        
        moveOrdering.ResetKillers();
        moveOrdering.DecayHistory();

        SetupTimeLimits(timer);
        
        searchStopwatch = Stopwatch.StartNew();
        
        int bestEval = 0;
        int depthSearched = 0;
        int totalAspirationReSearches = 0; // Track total re-searches for statistics
        int previousDepthScore = 0; // Track score stability
        
        // Iterative deepening loop
        for (int depth = 1; depth <= 256; depth++)
        {
            // Don't start a new iteration if the soft limit is hit
            if (searchStopwatch.ElapsedMilliseconds > softTimeLimit)
            {
                break;
            }
            
            int alpha = -9999999;
            int beta = 9999999;
            
            // Adaptive aspiration window sizing based on game phase and score stability
            int aspirationDelta = GetAdaptiveAspirationDelta(board, depth, bestEval, previousDepthScore);
            int originalAspirationDelta = aspirationDelta; // Store original for re-search logic
            
            // Use aspiration windows after depth 4 when we have a reliable score
            if (depth > 4 && Math.Abs(bestEval) < 29000 && aspirationDelta > 0) // aspirationDelta == 0 means disabled
            {
                alpha = bestEval - aspirationDelta;
                beta = bestEval + aspirationDelta;
            }
            
            int eval = 0;
            int aspirationReSearchCount = 0;
            
            // Aspiration window loop with gradual widening
            while (true)
            {
                eval = Search(board, depth, alpha, beta, true);
                
                // If search was cancelled, break out
                if (searchCanceledImmediately)
                    break;
                
                // Check if we're within the aspiration window
                if (eval > alpha && eval < beta)
                {
                    // Success! Score is within window
                    break;
                }
                
                // Failed outside window - need to re-search with wider bounds
                aspirationReSearchCount++;
                totalAspirationReSearches++;
                
                // Prevent infinite re-searches
                if (aspirationReSearchCount >= maxAspirationReSearches)
                {
                    // Give up and search with full window
                    alpha = -9999999;
                    beta = 9999999;
                    eval = Search(board, depth, alpha, beta, true);
                    break;
                }
                
                // Gradual widening based on which bound failed
                if (eval <= alpha)
                {
                    // Failed low - widen alpha
                    // if (!ChessChallenge.Application.Program.UCI_MODE)
                    //     Console.WriteLine($"Aspiration fail low at depth {depth}, re-search #{aspirationReSearchCount}: score {eval} <= alpha {alpha}");
                    
                    // Exponential widening for alpha
                    // In endgames (detected by larger initial window), widen more aggressively to avoid excessive re-searches
                    int wideningFactor = (originalAspirationDelta >= 100) ? 3 : 2; // More aggressive in endgames
                    aspirationDelta = aspirationDelta * wideningFactor;
                    alpha = Math.Max(-9999999, bestEval - aspirationDelta);
                    
                    // After multiple failures, open the window completely on the failing side
                    if (aspirationReSearchCount >= 3 || aspirationDelta > 500)
                        alpha = -9999999;
                }
                else if (eval >= beta)
                {
                    // Failed high - widen beta
                    // if (!ChessChallenge.Application.Program.UCI_MODE)
                    //     Console.WriteLine($"Aspiration fail high at depth {depth}, re-search #{aspirationReSearchCount}: score {eval} >= beta {beta}");
                    
                    // Exponential widening for beta
                    // In endgames (detected by larger initial window), widen more aggressively to avoid excessive re-searches
                    int wideningFactor = (originalAspirationDelta >= 100) ? 3 : 2; // More aggressive in endgames
                    aspirationDelta = aspirationDelta * wideningFactor;
                    beta = Math.Min(9999999, bestEval + aspirationDelta);
                    
                    // After multiple failures, open the window completely on the failing side
                    if (aspirationReSearchCount >= 3 || aspirationDelta > 500)
                        beta = 9999999;
                }
                
                // Reset best move for re-search
                bestMoveThisIteration = Move.NullMove;
            }
            
            depthSearched++;
            
            // Iteration fully completed
            if (!searchCanceledImmediately)
            {
                bestMove = bestMoveThisIteration;
                previousDepthScore = bestEval; // Store for score stability tracking
                bestEval = eval;
                
                // Output UCI info for completed depth
                OutputUCIInfo(depth, eval, bestMoveThisIteration, searchStopwatch.ElapsedMilliseconds, positionsEvaluated);
                
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
        {
            Console.WriteLine($"Depth searched: {depthSearched}, Final move: {bestMove.ToString()}, Time took: {searchStopwatch.ElapsedMilliseconds}ms, " +
                        $"Positions evaluated: {positionsEvaluated}, Score: {bestEval}");
            if (totalAspirationReSearches > 0)
                Console.WriteLine($"Aspiration window re-searches: {totalAspirationReSearches}");
        }

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
    
    int Search(Board board, int depth, int alpha, int beta, bool isRoot, int numExtensions = 0, int ply = 0, bool nullMoveAllowed = true)
    {
        // Note: This implementation uses fail-hard alpha-beta (returns exact bounds)
        // Aspiration windows still work with fail-hard, we just re-search when
        // the score equals a bound rather than only when it exceeds it
        
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
            return -immediateMateScore + depth;  // Add depth to prefer shorter mates
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
        
        // Null Move Pruning
        if (!isRoot && nullMoveAllowed && depth >= 3 && !board.IsInCheck() &&
            beta > -29000 && // Don't null move when opponent has a forced mate
            !IsPawnEndgame(board)) // Avoid null move in pawn endgames due to zugzwang
        {
            // Make null move
            board.ForceSkipTurn();
            
            // Adaptive reduction based on depth
            int R = 2;
            if (depth >= 5) R = 3;
            if (depth >= 12) R = 6;
            
            // Search with reduced depth and null window
            // We search with -beta, -beta+1 (null window) since we only care if it fails high
            int nullScore = -Search(board, depth - R - 1, -beta, -beta + 1, false, numExtensions, ply + 1, false);
            
            // Undo null move
            board.UndoSkipTurn();
            
            // If null move failed high (opponent couldn't punish our pass), we can prune
            if (nullScore >= beta && Math.Abs(nullScore) < 29000) // Don't trust mate scores from reduced search
            {
                return beta; // Null move cutoff
            }
        }
        
        Span<Move> movesSpan = stackalloc Move[218];
        board.GetLegalMovesNonAlloc(ref movesSpan);
        moveOrdering.OrderMoves(board, movesSpan, false, ply, ttMove);
        
        Move bestMoveThisPosition = Move.NullMove;
        int bestScore = int.MinValue;
        bool searchedPvMove = false; // Track if we've searched at least one move with full window
        
        for (int i = 0; i < movesSpan.Length; i++)
        {
            if (searchCanceledImmediately)
                break;
            
            Move move = movesSpan[i];
                
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

            int score;
            int newDepth = depth - 1 + extension;
            
            // Principal Variation Search
            if (searchedPvMove)
            {
                // For all moves after the first PV move, search with null window first
                // This is a scout search to see if the move is worth investigating further
                
                // Late Move Reductions
                int reduction = 0;
                if (extension == 0 && depth >= 3 && i > 3 && !move.IsCapture && !move.IsPromotion)
                {
                    reduction = 1;
                    if (i > 6) reduction = 2;  // More aggressive for later moves
                }
                
                // Null window search (also called scout search)
                // Note: nullMoveAllowed = true for normal moves
                score = -Search(board, newDepth - reduction, -alpha - 1, -alpha, false, numExtensions + extension, ply + 1);
                
                // If the move appears to be better than alpha (and no reduction was applied OR score > alpha despite reduction)
                // we need to re-search with full window to get accurate score
                if (score > alpha && (score < beta || reduction > 0))
                {
                    // Re-search with full window and no reduction
                    score = -Search(board, newDepth, -beta, -alpha, false, numExtensions + extension, ply + 1);
                }
            }
            else
            {
                // First move or PV node - search with full window
                score = -Search(board, newDepth, -beta, -alpha, false, numExtensions + extension, ply + 1);
                searchedPvMove = true;
            }
            
            board.UndoMove(move);
            
            if (searchCanceledImmediately)
                break;
            
            if (score > bestScore)
            {
                bestScore = score;
                bestMoveThisPosition = move;
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
                return beta; // fail-hard
            }

            if (score > alpha)
            {
                alpha = score;
                searchedPvMove = true; // We found a new best move, so this becomes our PV
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
            return -immediateMateScore;
        }
        
        if (board.IsDraw())
        {
            return 0;
        }
        
        positionsEvaluated++;
        int evaluation = Evaluation.Evaluate(board);
        
        if (evaluation >= beta)
            return beta;
        
        // Delta Pruning
        // If we're so far below alpha that even capturing the queen won't help, we can prune
        // This is the maximum material we could possibly gain from a single capture
        const int deltaMargin = 900; // Queen value
        
        if (evaluation < alpha - deltaMargin)
        {
            // We're so far behind that no single capture can help
            // Unless we're in check (then we must search all moves)
            if (!board.IsInCheck())
            {
                return alpha; // Fail-low, return alpha
            }
        }
        
        alpha = Math.Max(alpha, evaluation);
        
        Span<Move> capturesSpan = stackalloc Move[218];
        board.GetLegalMovesNonAlloc(ref capturesSpan, true);
        moveOrdering.OrderMoves(board, capturesSpan, true);
        
        foreach (Move capture in capturesSpan)
        {
            // Check cancellation before making a move
            if (searchCanceledImmediately)
                return evaluation;  // Return static eval when canceled
            
            // Futility Pruning (more granular delta pruning per move)
            // Skip captures that can't possibly raise alpha
            if (!board.IsInCheck() && !capture.IsPromotion)
            {
                // Estimate the maximum gain from this capture
                int capturedPieceValue = 0;
                if (capture.CapturePieceType != PieceType.None)
                {
                    capturedPieceValue = Evaluation.PieceValues[(int)capture.CapturePieceType];
                }
                
                // Add a small margin for positional gains
                const int futilityMargin = 50;
                
                // If even after capturing this piece we're still below alpha minus margin, skip it
                if (evaluation + capturedPieceValue + futilityMargin < alpha)
                {
                    continue; // Skip this capture, it won't help
                }
            }
            
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
    
    private int GetAdaptiveAspirationDelta(Board board, int depth, int currentScore, int previousScore)
    {
        // Count each piece type using bitboard operations
        ulong queens = board.GetPieceBitboard(PieceType.Queen, true) | board.GetPieceBitboard(PieceType.Queen, false);
        ulong rooks = board.GetPieceBitboard(PieceType.Rook, true) | board.GetPieceBitboard(PieceType.Rook, false);
        ulong bishops = board.GetPieceBitboard(PieceType.Bishop, true) | board.GetPieceBitboard(PieceType.Bishop, false);
        ulong knights = board.GetPieceBitboard(PieceType.Knight, true) | board.GetPieceBitboard(PieceType.Knight, false);
    
        // Use BitboardHelper to count bits (population count)
        int queenCount = BitboardHelper.GetNumberOfSetBits(queens);
        int rookCount = BitboardHelper.GetNumberOfSetBits(rooks);
        int bishopCount = BitboardHelper.GetNumberOfSetBits(bishops);
        int knightCount = BitboardHelper.GetNumberOfSetBits(knights);
    
        int minorPieces = bishopCount + knightCount;
        int totalMaterial = queenCount * 900 + rookCount * 500 + minorPieces * 300;
        
        // Determine game phase
        bool isEndgame = totalMaterial < 2600; // Roughly equivalent to 2 rooks + 2 minors per side
        bool isLateEndgame = totalMaterial < 1300; // Very few pieces left
        bool isPawnEndgame = queens == 0 && rooks == 0 && minorPieces <= 1;
        
        // Calculate score volatility (how much the score changed from last iteration)
        int scoreVolatility = 0;
        if (depth > 5 && previousScore != 0)
        {
            scoreVolatility = Math.Abs(currentScore - previousScore);
        }
        
        // Determine aspiration window size
        int aspirationDelta;
        
        if (isPawnEndgame || isLateEndgame)
        {
            // Very wide window or disable aspiration in pure pawn/late endgames
            // These positions are extremely volatile (passed pawns, promotion tactics)
            if (totalMaterial < 1000 || IsPawnEndgame(board))
            {
                // Disable aspiration windows in very late endgames
                aspirationDelta = 0; // 0 means disabled
            }
            else
            {
                aspirationDelta = 200; // Very wide window (2 pawns)
            }
        }
        else if (isEndgame)
        {
            // Wider window in endgames
            aspirationDelta = 120; // 1 pawn
            
            // Further adjust based on score volatility
            if (scoreVolatility > 150)
            {
                aspirationDelta = 150; // Even wider if scores are jumping around
            }
        }
        else
        {
            // Opening/Middlegame - use tighter windows
            aspirationDelta = 50;
            
            // Slightly widen if we're seeing volatility even in middlegame
            if (scoreVolatility > 100)
            {
                aspirationDelta = 85;
            }
        }
        
        // Special case: if we're close to mate scores, use wider windows
        // as tactics become more critical
        if (Math.Abs(currentScore) > 1000) // Getting close to mate territory
        {
            aspirationDelta = Math.Max(aspirationDelta, 150);
        }
        
        return aspirationDelta;
    }
    
    private bool IsPawnEndgame(Board board)
    {
        ulong pawns = board.GetPieceBitboard(PieceType.Pawn, true) | board.GetPieceBitboard(PieceType.Pawn, false);
        return BitboardHelper.GetNumberOfSetBits(board.AllPiecesBitboard) ==
               BitboardHelper.GetNumberOfSetBits(pawns) + 2;
    }


    private void SetupTimeLimits(Timer timer)
    {
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
    }
    
    private void OutputUCIInfo(int depth, int score, Move bestMove, long timeMs, int nodes)
    {
        if (!ChessChallenge.Application.Program.UCI_MODE) return;
        
        // Calculate nodes per second
        long nps = timeMs > 0 ? (nodes * 1000) / timeMs : 0;
        
        // Convert score to centipawns for UCI
        int centipawnScore = score;
        
        // Handle mate scores
        string scoreString;
        if (Math.Abs(score) > 29000)
        {
            int mateDistance = (immediateMateScore - Math.Abs(score));
            if (score > 0)
                scoreString = $"mate {mateDistance}";
            else
                scoreString = $"mate -{mateDistance}";
        }
        else
        {
            scoreString = $"cp {centipawnScore}";
        }
        
        Console.WriteLine($"info depth {depth} score {scoreString} time {timeMs} nodes {nodes} nps {nps} pv {bestMove}");
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