using ChessChallenge.API;
using System;

public class MoveOrdering
{
    public void OrderMoves(Span<Move> moves, Move hashMove = default)
    {
        moves.Sort((move1, move2) => GetMoveScore(move2, hashMove).CompareTo(GetMoveScore(move1, hashMove)));
    }
    
    int GetMoveScore(Move move, Move hashMove)
    {
        int score = 0;
        
        if (!hashMove.IsNull && move.Equals(hashMove))
        {
            return 100000; // Highest priority for TT hash move
        }
        
        if (move.IsPromotion)
        {
            score += 1000;
            score += Evaluation.PieceValues[(int)move.PromotionPieceType];
        }

        if (move.IsCastles)
        {
            score += 800;
        }
        
        if (move.IsCapture)
        {
            int capturedValue = Evaluation.PieceValues[(int)move.CapturePieceType];
            int attackerValue = Evaluation.PieceValues[(int)move.MovePieceType];
            score += capturedValue * 10 - attackerValue + 500; // Multiply victim value for better ordering
        }
        
        return score;
    }
}