using ChessChallenge.API;
using System;
using System.Diagnostics.CodeAnalysis;

public class MoveOrdering
{
    public const int maxKillerMovePly = 32;
    
    int[] moveScores;
    
    const int million = 1000000;
    const int hashMoveScore = 100 * million;
    const int winningCaptureBias = 8 * million;
    const int promoteBias = 6 * million;
    const int killerBias = 4 * million;
    const int losingCaptureBias = 2 * million;
    
    Killers[] killerMoves;
    public int[,,] history;

    public MoveOrdering()
    {
        moveScores = new int[218];
        killerMoves = new Killers[maxKillerMovePly];
        history = new int[2, 64, 64];
    }
    
    public void OrderMoves(Board board, Span<Move> moves, bool inQSearch, int ply = 0, Move hashMove = default)
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
            int toScore = Evaluation.GetPieceSquareValue(movePieceType, Evaluation.GetSquareIndexSided(targetSquare, board.IsWhiteToMove), openingWeight, endGameWeight);
            int fromScore = Evaluation.GetPieceSquareValue(movePieceType, Evaluation.GetSquareIndexSided(startSquare, board.IsWhiteToMove), openingWeight, endGameWeight);
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

            if (!isCapture && !move.IsPromotion)
            {
                // Killer moves
                if (!inQSearch && ply < maxKillerMovePly && killerMoves[ply].Match(move))
                {
                    score += killerBias;
                }
                
                // History heuristic
                int colorIndex = board.IsWhiteToMove ? 0 : 1;
                score += history[colorIndex, startSquare.Index, targetSquare.Index];
            }
            
            moveScores[i] = score;
        }
        
        Quicksort(moves, moveScores, 0, moves.Length - 1);
    }
    
    public void ResetKillers()
    {
        for (int i = 0; i < killerMoves.Length; i++) killerMoves[i].Clear();
    }

    public void AddKiller(int ply, Move move)
    {
        if ((uint)ply < (uint)killerMoves.Length) killerMoves[ply].Add(move);
    }

    public void ResetHistory()
    {
        history = new int[2, 64, 64];
    }

    public void DecayHistory()
    {
        for (int color = 0; color < 2; color++)
        {
            for (int from = 0; from < 64; from++)
            {
                for (int to = 0; to < 64; to++)
                {
                    history[color, from, to] /= 8;
                }
            }
        }
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

public struct Killers
{
    public Move moveA;
    public Move moveB;

    public void Add(Move m)
    {
        // Keep most-recent in A. Skip if it's already A.
        if (!m.Equals(moveA))
        {
            moveB = moveA;
            moveA = m;
        }
    }

    public bool Match(Move m) => m.Equals(moveA) || m.Equals(moveB);

    public void Clear() { moveA = Move.NullMove; moveB = Move.NullMove; }
}
