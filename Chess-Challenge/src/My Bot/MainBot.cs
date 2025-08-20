using ChessChallenge.API;
using System;
using System.Diagnostics;

public class MainBot : IChessBot
{
    Move bestMoveRoot;
    int positionsEvaluated;
    TranspositionTable? transpositionTable;
    
    public Move Think(Board board, Timer timer)
    {
        positionsEvaluated = 0;
        
        if (transpositionTable == null)
            transpositionTable = new TranspositionTable(64);
        
        Stopwatch stopwatch = Stopwatch.StartNew();
        
        Search(board, 5, -9999999, 9999999, true);
        
        stopwatch.Stop();
        Console.WriteLine($"Best move: {bestMoveRoot}, Time took: {stopwatch.ElapsedMilliseconds}ms, Positions evaluated: {positionsEvaluated}");
        Console.WriteLine($"Transposition count: {transpositionTable.GetCount()}");
        
        return bestMoveRoot;
    }
    
    int Search(Board board, int depth, int alpha, int beta, bool isRoot)
    {
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
            return board.IsWhiteToMove ? -30000 + depth : 30000 - depth;
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
        
        Move bestMove = moves[0];
        int bestValue = int.MinValue;
        
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int value = -Search(board, depth - 1, -beta, -alpha, false);
            board.UndoMove(move);
            
            if (value > bestValue)
            {
                bestValue = value;
                bestMove = move;
            }
            
            alpha = Math.Max(alpha, value);
            if (beta <= alpha)
                break;
        }
        
        NodeType nodeType = bestValue <= originalAlpha ? NodeType.UpperBound :
                           bestValue >= beta ? NodeType.LowerBound : NodeType.Exact;
        
        transpositionTable.StoreEntry(zobristKey, bestValue, depth, nodeType, bestMove);
        
        if (isRoot)
            bestMoveRoot = bestMove;
            
        return bestValue;
    }
    
    int QuiescenceSearch(Board board, int alpha, int beta)
    {
        positionsEvaluated++;
        
        if (board.IsInCheckmate())
        {
            return board.IsWhiteToMove ? -30000 : 30000;
        }
        
        if (board.IsDraw())
        {
            return 0;
        }
        
        int evaluation = Evaluation.Evaluate(board);
        
        if (evaluation >= beta)
            return beta;
        
        alpha = Math.Max(alpha, evaluation);
        
        Move[] captures = board.GetLegalMoves(true);
        MoveOrdering.OrderMoves(captures);
        
        foreach (Move capture in captures)
        {
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