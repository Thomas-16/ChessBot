using ChessChallenge.API;
using System;

public class MoveOrdering
{
    int[] moveScores;
    
    const int million = 1000000;
    const int hashMoveScore = 100 * million;
    const int winningCaptureBias = 8 * million;
    const int promoteBias = 6 * million;
    const int killerBias = 4 * million; // To be implemented
    const int losingCaptureBias = 2 * million;
    const int regularBias = 0; // To be implemented

    public MoveOrdering()
    {
        moveScores = new int[218];
    }
    
    public void OrderMoves(Board board, ref Span<Move> moves, Move hashMove = default)
    {
        ulong oppAttacks = board.GetOpponentAttackMap();
        
        // Calculate game phase (0 = opening, 1 = endgame)
        // Based on remaining material
        float endGameWeight = Evaluation.CalculateGamePhase(board);
        float openingWeight = 1f - endGameWeight;

        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];

            if (move.Equals(hashMove))
            {
                moveScores[i] = hashMoveScore;
                continue;
            }

            int score = 0;
            Square startSquare = move.StartSquare;
            Square targetSquare = move.TargetSquare;
            int startSquareIndex = Evaluation.GetSquareIndex(startSquare, board.IsWhiteToMove);
            int targetSquareIndex = Evaluation.GetSquareIndex(targetSquare, board.IsWhiteToMove);
            
            PieceType movePieceType = move.MovePieceType;
            PieceType capturePieceType = move.CapturePieceType;
            bool isCapture = capturePieceType != PieceType.None;
            int pieceValue = GetPieceValue(movePieceType);

            if (isCapture)
            {
                // Order moves to try capturing the most valuable opponent piece with least valuable of own pieces first
                int captureMaterialDelta = GetPieceValue(capturePieceType) - pieceValue;
                bool opponentCanRecapture = BitboardHelper.SquareIsSet(oppAttacks, targetSquare);
                if (opponentCanRecapture)
                {
                    score += (captureMaterialDelta >= 0 ? winningCaptureBias : losingCaptureBias) + captureMaterialDelta;
                }
                else
                {
                    score += winningCaptureBias + captureMaterialDelta;
                }
            }
            
            // Promotion bias
            if (move.IsPromotion)
            {
                score += promoteBias;
            }
            
            // Encourage moving into a better square(based on PST)
            int toScore = Evaluation.GetPieceSquareValue(movePieceType, targetSquareIndex, openingWeight, endGameWeight);
            int fromScore = Evaluation.GetPieceSquareValue(movePieceType, startSquareIndex, openingWeight, endGameWeight);
            score += toScore - fromScore;
            
            // Is not a pawn or a king and is going into a square the enemy is attacking
            if (movePieceType != PieceType.King && movePieceType != PieceType.Pawn && BitboardHelper.SquareIsSet(oppAttacks, targetSquare))
            {
                score -= 20;
            }
            
            // Encourage castling
            if (move.IsCastles)
            {
                score += 70;
            }
            
            // Add original array index as tie-breaker
            moveScores[i] = score;
        }
        
        Quicksort(moves, moveScores, 0, moves.Length - 1);
    }

    int GetPieceValue(PieceType pieceType)
    {
        return Evaluation.PieceValues[(int)pieceType];
    }
    
    static void Quicksort(Span<Move> values, int[] scores, int low, int high)
    {
        if (low < high)
        {
            int pivotIndex = Partition(values, scores, low, high);
            Quicksort(values, scores, low, pivotIndex - 1);
            Quicksort(values, scores, pivotIndex + 1, high);
        }
    }

    static int Partition(Span<Move> values, int[] scores, int low, int high)
    {
        int pivotScore = scores[high];
        int i = low - 1;

        for (int j = low; j <= high - 1; j++)
        {
            if (scores[j] > pivotScore)
            {
                i++;
                (values[i], values[j]) = (values[j], values[i]);
                (scores[i], scores[j]) = (scores[j], scores[i]);
            }
        }
        (values[i + 1], values[high]) = (values[high], values[i + 1]);
        (scores[i + 1], scores[high]) = (scores[high], scores[i + 1]);

        return i + 1;
    }
}