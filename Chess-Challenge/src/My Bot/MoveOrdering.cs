using ChessChallenge.API;
using System;

public static class MoveOrdering
{
    public static void OrderMoves(Move[] moves)
    {
        Array.Sort(moves, (move1, move2) => GetMoveScore(move2).CompareTo(GetMoveScore(move1)));
    }
    
    static int GetMoveScore(Move move)
    {
        int score = 0;
        
        if (move.IsPromotion)
        {
            score += 1000;
            score += Evaluation.PieceValues[(int)move.PromotionPieceType];
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