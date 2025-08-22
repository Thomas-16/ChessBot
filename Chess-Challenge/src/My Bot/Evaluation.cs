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
    static readonly ulong centerSquares = ((ulong)1 << new Square("e4").Index)
                          | ((ulong)1 << new Square("d4").Index)
                          | ((ulong)1 << new Square("e5").Index)
                          | ((ulong)1 << new Square("d5").Index)
                          | ((ulong)1 << new Square("c3").Index)
                          | ((ulong)1 << new Square("f3").Index)
                          | ((ulong)1 << new Square("c6").Index)
                          | ((ulong)1 << new Square("f6").Index)
                          | ((ulong)1 << new Square("c4").Index)
                          | ((ulong)1 << new Square("f4").Index)
                          | ((ulong)1 << new Square("c5").Index)
                          | ((ulong)1 << new Square("f5").Index)
                          | ((ulong)1 << new Square("d3").Index)
                          | ((ulong)1 << new Square("e3").Index)
                          | ((ulong)1 << new Square("d6").Index)
                          | ((ulong)1 << new Square("e6").Index);

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
        
        // Calculate game phase (0 = opening, 1 = endgame)
        // Based on remaining material
        int gamePhase = CalculateGamePhase(board);
        float endGameWeight = gamePhase / 256f;  // Convert to 0-1 range
        float openingWeight = 1f - endGameWeight;
        
        PieceList[] allPieceLists = board.GetAllPieceLists();
        
        for (int i = 0; i < allPieceLists.Length; i++)
        {
            PieceList pieceList = allPieceLists[i];
            PieceType pieceType = pieceList.TypeOfPieceInList;
            int pieceValue = PieceValues[(int)pieceType];
            bool isWhite = pieceList.IsWhitePieceList;
            
            totalPieces += pieceList.Count;
            
            // Material evaluation
            int materialValue = pieceValue * pieceList.Count;
            
            // Piece-square table evaluation
            int pstValue = 0;
            for (int j = 0; j < pieceList.Count; j++)
            {
                Square square = pieceList[j].Square;
                int squareIndex = GetSquareIndex(square, isWhite);
                
                // Get piece-square value with game phase blending
                int pieceSquareValue = GetPieceSquareValue(pieceType, squareIndex, openingWeight, endGameWeight);
                pstValue += pieceSquareValue;
            }
            
            // Add to evaluation (positive for white, negative for black)
            if (isWhite)
            {
                evaluation += materialValue + pstValue;
            }
            else
            {
                evaluation -= materialValue + pstValue;
            }
        }
        
        ulong whitePieces = board.WhitePiecesBitboard;
        ulong blackPieces = board.BlackPiecesBitboard;
        
        int whiteCenterControlScore = BitboardHelper.GetNumberOfSetBits(whitePieces & centerSquares);
        int blackCenterControlScore = BitboardHelper.GetNumberOfSetBits(blackPieces & centerSquares);
        
        // In the opening and midgame, add score for center control
        if (totalPieces < 25) {
            float openingScalingFactor = Math.Clamp(totalPieces / 20f, 0f, 1f);

            evaluation += (int) Math.Round((whiteCenterControlScore - blackCenterControlScore) * 20f * openingScalingFactor);
        }
        
        // Mop-up evaluation for endgame
        evaluation += EvaluateMopUp(board, evaluation, endGameWeight);
        
        return board.IsWhiteToMove ? evaluation : -evaluation;
    }
    
    private static int CalculateGamePhase(Board board)
    {
        // Game phase calculation based on remaining material
        // Start at 256 (opening) and decrease as pieces are captured
        int phase = 256;
        
        PieceList[] allPieceLists = board.GetAllPieceLists();
        
        for (int i = 0; i < allPieceLists.Length; i++)
        {
            PieceList pieceList = allPieceLists[i];
            PieceType pieceType = pieceList.TypeOfPieceInList;
            
            // Subtract phase value for each piece
            // Knights and Bishops reduce phase by 16
            // Rooks reduce phase by 32
            // Queens reduce phase by 64
            // Pawns don't affect phase (encourage pawn endgames)
            // Kings don't affect phase
            
            switch (pieceType)
            {
                case PieceType.Knight:
                case PieceType.Bishop:
                    phase -= pieceList.Count * 16;
                    break;
                case PieceType.Rook:
                    phase -= pieceList.Count * 32;
                    break;
                case PieceType.Queen:
                    phase -= pieceList.Count * 64;
                    break;
            }
        }
        
        // Clamp to valid range [0, 256]
        return Math.Max(0, Math.Min(256, phase));
    }
    
    private static int GetSquareIndex(Square square, bool isWhite)
    {
        // For white, use normal index (a1=0, h8=63)
        // For black, flip the board vertically (a8=0, h1=63)
        int index = square.Index;
        
        if (!isWhite)
        {
            // Flip rank for black pieces
            int rank = index / 8;
            int file = index % 8;
            int flippedRank = 7 - rank;
            index = flippedRank * 8 + file;
        }
        
        return index;
    }
    
    private static int GetPieceSquareValue(PieceType pieceType, int squareIndex, float openingWeight, float endGameWeight)
    {
        int openingValue = 0;
        int endgameValue = 0;
        
        switch (pieceType)
        {
            case PieceType.Pawn:
                openingValue = PieceSquareTable.Pawns[squareIndex];
                endgameValue = PieceSquareTable.PawnsEnd[squareIndex];
                break;
            case PieceType.Knight:
                openingValue = PieceSquareTable.Knights[squareIndex];
                endgameValue = PieceSquareTable.Knights[squareIndex]; // Same for all phases
                break;
            case PieceType.Bishop:
                openingValue = PieceSquareTable.Bishops[squareIndex];
                endgameValue = PieceSquareTable.Bishops[squareIndex]; // Same for all phases
                break;
            case PieceType.Rook:
                openingValue = PieceSquareTable.Rooks[squareIndex];
                endgameValue = PieceSquareTable.Rooks[squareIndex]; // Same for all phases
                break;
            case PieceType.Queen:
                openingValue = PieceSquareTable.Queens[squareIndex];
                endgameValue = PieceSquareTable.Queens[squareIndex]; // Same for all phases
                break;
            case PieceType.King:
                openingValue = PieceSquareTable.KingStart[squareIndex];
                endgameValue = PieceSquareTable.KingEnd[squareIndex];
                break;
            default:
                return 0;
        }
        
        // Blend between opening and endgame values
        return (int)(openingValue * openingWeight + endgameValue * endGameWeight);
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