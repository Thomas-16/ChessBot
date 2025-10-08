namespace Chess_Backend.Models
{
    public class ChessPositionContext
    {
        public string PlayerColor { get; set; } = "White";
        public int MoveNumber { get; set; }
        public string TurnToMove { get; set; } = "White";
        public int PlayerMaterialCentipawns { get; set; }
        public int OpponentMaterialCentipawns { get; set; }
        public string[] PlayerPieces { get; set; } = [];
        public string[] OpponentPieces { get; set; } = [];
        public string[] RecentMoves { get; set; } = [];
        public string GamePhase { get; set; } = "Opening";
        public bool IsInCheck { get; set; }
        public int? LegalMoveCount { get; set; }
    }
}
