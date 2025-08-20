using ChessChallenge.API;
using System;

public class MainBot : IChessBot
{
    Move bestMoveRoot;
    
    public Move Think(Board board, Timer timer)
    {
        Search(board, 4, int.MinValue, int.MaxValue, true);
        return bestMoveRoot;
    }
    
    int Search(Board board, int depth, int alpha, int beta, bool isRoot)
    {
        if (depth == 0 || board.IsInCheckmate() || board.IsDraw())
        {
            return Evaluation.Evaluate(board);
        }
        
        Move[] moves = board.GetLegalMoves();
        MoveOrdering.OrderMoves(moves);
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
        
        if (isRoot)
            bestMoveRoot = bestMove;
            
        return bestValue;
    }
}