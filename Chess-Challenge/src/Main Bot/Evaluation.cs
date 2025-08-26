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
    
    static readonly int[] passedPawnBonuses = { 0, 120, 80, 50, 30, 15, 15 }; // bonus given to a passed pawn based on how close it is to promoting

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
        
        // Calculate game phase (0 = opening, 1 = endgame)
        // Based on remaining material
        int gamePhase = CalculateGamePhase(board);
        float endGameWeight = gamePhase / 256f;  // Convert to 0-1 range
        float openingWeight = 1f - endGameWeight;
        
        // Get all piece bitboards once for efficiency
        ulong whitePawns = board.GetPieceBitboard(PieceType.Pawn, true);
        ulong blackPawns = board.GetPieceBitboard(PieceType.Pawn, false);
        ulong whiteKnights = board.GetPieceBitboard(PieceType.Knight, true);
        ulong blackKnights = board.GetPieceBitboard(PieceType.Knight, false);
        ulong whiteBishops = board.GetPieceBitboard(PieceType.Bishop, true);
        ulong blackBishops = board.GetPieceBitboard(PieceType.Bishop, false);
        ulong whiteRooks = board.GetPieceBitboard(PieceType.Rook, true);
        ulong blackRooks = board.GetPieceBitboard(PieceType.Rook, false);
        ulong whiteQueens = board.GetPieceBitboard(PieceType.Queen, true);
        ulong blackQueens = board.GetPieceBitboard(PieceType.Queen, false);
        
        // Evaluate each piece type
        evaluation += EvaluatePieceTypeBitboard(whitePawns, true, PieceType.Pawn, openingWeight, endGameWeight);
        evaluation -= EvaluatePieceTypeBitboard(blackPawns, false, PieceType.Pawn, openingWeight, endGameWeight);
        evaluation += EvaluatePieceTypeBitboard(whiteKnights, true, PieceType.Knight, openingWeight, endGameWeight);
        evaluation -= EvaluatePieceTypeBitboard(blackKnights, false, PieceType.Knight, openingWeight, endGameWeight);
        evaluation += EvaluatePieceTypeBitboard(whiteBishops, true, PieceType.Bishop, openingWeight, endGameWeight);
        evaluation -= EvaluatePieceTypeBitboard(blackBishops, false, PieceType.Bishop, openingWeight, endGameWeight);
        evaluation += EvaluatePieceTypeBitboard(whiteRooks, true, PieceType.Rook, openingWeight, endGameWeight);
        evaluation -= EvaluatePieceTypeBitboard(blackRooks, false, PieceType.Rook, openingWeight, endGameWeight);
        evaluation += EvaluatePieceTypeBitboard(whiteQueens, true, PieceType.Queen, openingWeight, endGameWeight);
        evaluation -= EvaluatePieceTypeBitboard(blackQueens, false, PieceType.Queen, openingWeight, endGameWeight);
        
        // King evaluation - get squares and evaluate normally
        Square whiteKing = board.GetKingSquare(true);
        Square blackKing = board.GetKingSquare(false);
        
        int whiteKingSquareIndex = GetSquareIndex(whiteKing, true);
        int blackKingSquareIndex = GetSquareIndex(blackKing, false);
        
        int whiteKingValue = GetPieceSquareValue(PieceType.King, whiteKingSquareIndex, openingWeight, endGameWeight);
        int blackKingValue = GetPieceSquareValue(PieceType.King, blackKingSquareIndex, openingWeight, endGameWeight);
        
        evaluation += whiteKingValue - blackKingValue;

        ulong whitePiecesWithoutKing = board.WhitePiecesBitboard & ~board.GetPieceBitboard(PieceType.King, true);
        ulong blackPiecesWithoutKing = board.BlackPiecesBitboard & ~board.GetPieceBitboard(PieceType.King, false);

        // Center control
        int pawnCenterControlScore = 12;
        int pieceCenterControlScore = 10;
        int whiteCenterControl = BitboardHelper.GetNumberOfSetBits((whitePiecesWithoutKing & ~whitePawns) & centerSquares) * pieceCenterControlScore
                                 + BitboardHelper.GetNumberOfSetBits(whitePawns & centerSquares) * pawnCenterControlScore;
        int blackCenterControl = BitboardHelper.GetNumberOfSetBits((blackPiecesWithoutKing & ~blackPawns) & centerSquares) * pieceCenterControlScore
                                 + BitboardHelper.GetNumberOfSetBits(blackPawns & centerSquares) * pawnCenterControlScore;
        
        // Passed pawns and isolated pawns
        int whitePawnScore = EvaluatePassedAndIsolatedPawns(whitePawns, blackPawns, true);
        int blackPawnScore = EvaluatePassedAndIsolatedPawns(blackPawns, whitePawns, false);
        evaluation += whitePawnScore - blackPawnScore;

        // Apply with opening weight (important early, less important in endgame)
        evaluation += (int)((whiteCenterControl - blackCenterControl) * openingWeight);
        
        // Mop-up evaluation for endgame
        evaluation += EvaluateMopUp(board, evaluation, endGameWeight);
        
        return board.IsWhiteToMove ? evaluation : -evaluation;
    }
    
    private static int EvaluatePieceTypeBitboard(ulong pieces, bool isWhite, PieceType pieceType, float openingWeight, float endGameWeight)
    {
        int evaluation = 0;
        int pieceValue = PieceValues[(int)pieceType];
        
        // Material evaluation
        int pieceCount = BitboardHelper.GetNumberOfSetBits(pieces);
        evaluation += pieceValue * pieceCount;
        
        // Piece-square table evaluation using bitboard iteration
        ulong piecesCopy = pieces;
        while (piecesCopy != 0)
        {
            int squareIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref piecesCopy);
            int adjustedSquareIndex = GetSquareIndex(squareIndex, isWhite);
            
            // Get piece-square value with game phase blending
            int pieceSquareValue = GetPieceSquareValue(pieceType, adjustedSquareIndex, openingWeight, endGameWeight);
            evaluation += pieceSquareValue;
        }
        
        return evaluation;
    }
    
    private static int EvaluatePassedAndIsolatedPawns(ulong ownPawns, ulong opponentPawns, bool isWhite)
    {
        int evaluation = 0;
        
        ulong pawnsCopy = ownPawns;
        while (pawnsCopy != 0)
        {
            int squareIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref pawnsCopy);
            ulong passedPawnMask = isWhite ? PrecomputedEvalData.WhitePassedPawnMask[squareIndex] : PrecomputedEvalData.BlackPassedPawnMask[squareIndex];
            
            // If no enemy pawns can block or capture this pawn, it's passed
            if ((passedPawnMask & opponentPawns) == 0)
            {
                // Bonus based on how advanced the pawn is
                int rank = squareIndex / 8;
                int bonus = isWhite ? passedPawnBonuses[7 - rank] : passedPawnBonuses[rank];
                evaluation += bonus;
            }
        }
        
        return evaluation;
    }
    
    public static int CalculateGamePhase(Board board)
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
    
    public static int GetSquareIndex(Square square, bool isWhite)
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
    
    public static int GetSquareIndex(int squareIndex, bool isWhite)
    {
        if (!isWhite)
        {
            // Flip rank for black pieces
            int rank = squareIndex / 8;
            int file = squareIndex % 8;
            int flippedRank = 7 - rank;
            return flippedRank * 8 + file;
        }
        
        return squareIndex;
    }
    
    public static int GetPieceSquareValue(PieceType pieceType, int squareIndex, float openingWeight, float endGameWeight)
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