using ChessChallenge.API;

public static class Evaluation
{
    public static readonly int[] PieceValues = { 0, 100, 320, 330, 500, 900, 0 };

    public static int Evaluate(Board board)
    {
        if (board.IsInCheckmate())
        {
            return board.IsWhiteToMove ? -30000 : 30000;
        }
        
        if (board.IsDraw())
        {
            return 0;
        }
        
        int evaluation = 0;
        
        PieceList[] allPieceLists = board.GetAllPieceLists();
        
        for (int i = 0; i < allPieceLists.Length; i++)
        {
            PieceList pieceList = allPieceLists[i];
            int pieceValue = PieceValues[(int)pieceList.TypeOfPieceInList];
            int materialValue = pieceValue * pieceList.Count;
            
            if (pieceList.IsWhitePieceList)
                evaluation += materialValue;
            else
                evaluation -= materialValue;
        }
        
        return board.IsWhiteToMove ? evaluation : -evaluation;
    }
    
}