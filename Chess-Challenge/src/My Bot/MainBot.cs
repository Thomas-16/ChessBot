using ChessChallenge.API;
using System;
using System.Diagnostics;

public class MainBot : IChessBot
{
    Move bestMoveThisIteration;
    Move bestMove; // Best move so far
    
    TranspositionTable? transpositionTable;
    
    Stopwatch searchStopwatch;
    int timeLimit = 200; // ms
    bool searchCanceled;
    
    int positionsEvaluated;
    
    public Move Think(Board board, Timer timer)
    {
        positionsEvaluated = 0;
        bestMoveThisIteration = Move.NullMove;
        bestMove = Move.NullMove;
        searchCanceled = false;
        
        if (transpositionTable == null)
            transpositionTable = new TranspositionTable(64);
        
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
                
                // Stop if we found a checkmate
                if (Math.Abs(eval) > 29000)
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
                    bestMove = bestMoveThisIteration;
                    bestEval = eval;
                }
                break;
            }
        }
        
        searchStopwatch.Stop();
        Console.WriteLine($"Depth searched: {depthSearched}, Final move: {bestMove.ToString()}, Time took: {searchStopwatch.ElapsedMilliseconds}ms, " +
                        $"Positions evaluated: {positionsEvaluated}, Score: {bestEval}");

        if (bestMove == Move.NullMove)
        {
            bestMove = board.GetLegalMoves()[0];
            Console.WriteLine("Best move not found, using first legal move");
        }

        return bestMove;
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
        
        Move[] moves = board.GetLegalMoves();
        MoveOrdering.OrderMoves(moves);
        
        // Prioritize the best move from previous iteration (from TT)
        if (!ttMove.IsNull)
        {
            for (int i = 0; i < moves.Length; i++)
            {
                if (moves[i].Equals(ttMove))
                {
                    (moves[0], moves[i]) = (moves[i], moves[0]);
                    break;
                }
            }
        }
        // At root, also prioritize the best move from previous iteration
        else if (isRoot && !this.bestMove.IsNull)
        {
            for (int i = 0; i < moves.Length; i++)
            {
                if (moves[i].Equals(this.bestMove))
                {
                    (moves[0], moves[i]) = (moves[i], moves[0]);
                    break;
                }
            }
        }
        
        Move bestMoveThisPosition = moves.Length > 0 ? moves[0] : Move.NullMove;
        int bestValue = int.MinValue;
        
        foreach (Move move in moves)
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
        
        Move[] captures = board.GetLegalMoves(true);
        MoveOrdering.OrderMoves(captures);
        
        foreach (Move capture in captures)
        {
            if (searchCanceled)
                break;
                
            board.MakeMove(capture);
            int value = -QuiescenceSearch(board, -beta, -alpha);
            board.UndoMove(capture);
            
            if (value >= beta)
                return beta;
            
            alpha = Math.Max(alpha, value);
        }
        
        return alpha;
    }
}