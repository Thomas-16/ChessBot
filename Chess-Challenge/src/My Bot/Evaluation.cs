using ChessChallenge.API;

public static class Evaluation
{
    public static readonly int[] PieceValues = { 0, 100, 320, 330, 500, 900, 0 };

    public static int Evaluate(Board board)
    {
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
        
        return evaluation;
    }
    
}