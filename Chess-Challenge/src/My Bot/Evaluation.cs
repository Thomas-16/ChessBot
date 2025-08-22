using ChessChallenge.API;
using System;

public static class Evaluation
{
    public static readonly int[] PieceValues = { 0, 100, 320, 330, 500, 900, 0 };
    
    static readonly int[] DistanceFromCenterTable = {
        3, 3, 3, 3, 3, 3, 3, 3,
        3, 2, 2, 2, 2, 2, 2, 3,
        3, 2, 1, 1, 1, 1, 2, 3,
        3, 2, 1, 0, 0, 1, 2, 3,
        3, 2, 1, 0, 0, 1, 2, 3,
        3, 2, 1, 1, 1, 1, 2, 3,
        3, 2, 2, 2, 2, 2, 2, 3,
        3, 3, 3, 3, 3, 3, 3, 3
    };

    public static int Evaluate(Board board)
    {
        if (board.IsInCheckmate())
        {
            // Return from the perspective of the side to move
            return -30000;  // Being checkmated is bad
        }
        
        if (board.IsDraw())
        {
            return 0;
        }
        
        int evaluation = 0;
        int totalPieces = 0;
        
        PieceList[] allPieceLists = board.GetAllPieceLists();
        
        for (int i = 0; i < allPieceLists.Length; i++)
        {
            PieceList pieceList = allPieceLists[i];
            int pieceValue = PieceValues[(int)pieceList.TypeOfPieceInList];
            int materialValue = pieceValue * pieceList.Count;
            totalPieces += pieceList.Count;
            
            if (pieceList.IsWhitePieceList)
                evaluation += materialValue;
            else
                evaluation -= materialValue;
        }
        
        // Count non-king pieces only
        int nonKingPieces = totalPieces - 2;
        float endGameWeight = 1f - Math.Min(1f, nonKingPieces / 14f);
        
        evaluation += EvaluateMopUp(board, evaluation, endGameWeight);
        
        return board.IsWhiteToMove ? evaluation : -evaluation;
    }

    private static int EvaluateMopUp(Board board, int evaluation, float endGameWeight)
    {
        if (endGameWeight <= 0.1f)
            return 0;
        
        Square whiteKing = board.GetKingSquare(true);
        Square blackKing = board.GetKingSquare(false);
        
        int whiteKingFile = whiteKing.File;
        int whiteKingRank = whiteKing.Rank;
        int blackKingFile = blackKing.File;
        int blackKingRank = blackKing.Rank;
        
        int kingDistance = Math.Abs(whiteKingFile - blackKingFile) + Math.Abs(whiteKingRank - blackKingRank);
        
        if (evaluation > 200)
        {
            int blackKingToEdge = DistanceFromCenterTable[blackKing.Index];
            int mopUpScore = blackKingToEdge * 20 + (7 - kingDistance) * 10;
            return (int)(mopUpScore * endGameWeight);
        }
        if (evaluation < -200)
        {
            int whiteKingToEdge = DistanceFromCenterTable[whiteKing.Index];
            int mopUpScore = whiteKingToEdge * 20 + (7 - kingDistance) * 10;
            return -(int)(mopUpScore * endGameWeight);
        }
        
        return 0;
    }
    
}