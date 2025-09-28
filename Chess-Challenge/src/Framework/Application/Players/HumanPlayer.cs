using ChessChallenge.Chess;
using Raylib_cs;
using System.Numerics;

namespace ChessChallenge.Application
{
    public class HumanPlayer
    {
        public event System.Action<Move>? MoveChosen;

        readonly Board board;
        readonly BoardUI boardUI;

        // State
        bool isDragging;
        int selectedSquare;
        bool isTurnToMove;

        // Click-and-click state
        bool hasClickSelection;
        int clickSelectedSquare;


        public HumanPlayer(BoardUI boardUI)
        {
            board = new();
            board.LoadStartPosition();
            this.boardUI = boardUI;
        }

        public void NotifyTurnToMove()
        {
            isTurnToMove = true;
            // Clear any previous selection states when it becomes our turn
            ClearAllStates();
        }

        void ClearAllStates()
        {
            isDragging = false;
            hasClickSelection = false;
            boardUI.ResetSquareColours(true);
        }

        public void SetPosition(string fen)
        {
            board.LoadPosition(fen);
        }

        public void Update()
        {
            if (!isTurnToMove)
            {
                return;
            }
            Vector2 mouseScreenPos = Raylib.GetMousePosition();
            Vector2 mouseWorldPos = Program.ScreenToWorldPos(mouseScreenPos);

            // Handle right click - cancels both drag and click selection
            if (RightMousePressedThisFrame())
            {
                if (isDragging)
                {
                    CancelDrag();
                }
                else if (hasClickSelection)
                {
                    CancelClickSelection();
                }
                return;
            }

            // Handle left mouse press
            if (LeftMousePressedThisFrame())
            {
                if (boardUI.TryGetSquareAtPoint(mouseWorldPos, out int square))
                {
                    HandleLeftMousePress(square, mouseWorldPos);
                }
                else if (hasClickSelection)
                {
                    // Clicked outside board - cancel selection
                    CancelClickSelection();
                }
            }

            // Handle dragging (if in drag mode)
            if (isDragging)
            {
                if (LeftMouseReleasedThisFrame())
                {
                    // Mouse released - determine if this was a click or drag
                    if (boardUI.TryGetSquareAtPoint(mouseWorldPos, out int square))
                    {
                        if (square == selectedSquare)
                        {
                            // Released on same square - convert to click selection
                            isDragging = false;
                            StartClickSelection(square);
                        }
                        else
                        {
                            // Released on different square - complete drag move
                            CancelDrag();
                            TryMakeMove(selectedSquare, square);
                        }
                    }
                    else
                    {
                        // Released outside board - cancel drag
                        CancelDrag();
                    }
                }
                else
                {
                    // Still dragging - show piece at mouse position
                    boardUI.DragPiece(selectedSquare, mouseWorldPos);
                }
            }
        }

        void CancelDrag()
        {
            isDragging = false;
            boardUI.ResetSquareColours(true);
        }

        void CancelClickSelection()
        {
            hasClickSelection = false;
            boardUI.ResetSquareColours(true);
        }

        void HandleLeftMousePress(int square, Vector2 mouseWorldPos)
        {
            int piece = board.Square[square];
            bool isOwnPiece = PieceHelper.IsColour(piece, board.IsWhiteToMove ? PieceHelper.White : PieceHelper.Black);

            if (!hasClickSelection)
            {
                // No current selection
                if (isOwnPiece)
                {
                    // Start drag mode if clicking on own piece
                    isDragging = true;
                    selectedSquare = square;
                    boardUI.HighlightLegalMoves(board, square);
                }
            }
            else
            {
                // Already have a click selection - handle second click
                if (square == clickSelectedSquare)
                {
                    // Clicked same square - deselect
                    CancelClickSelection();
                }
                else if (isOwnPiece)
                {
                    // Clicked different own piece - clear selection and start drag
                    CancelClickSelection();
                    isDragging = true;
                    selectedSquare = square;
                    boardUI.HighlightLegalMoves(board, square);
                }
                else
                {
                    // Clicked different square - try to move
                    if (TryMakeMove(clickSelectedSquare, square))
                    {
                        // Move was successful, clear selection
                        hasClickSelection = false;
                        boardUI.ResetSquareColours(true);
                    }
                    else
                    {
                        // Invalid move - cancel selection
                        CancelClickSelection();
                    }
                }
            }
        }

        void StartClickSelection(int square)
        {
            // Clear any previous selection highlights first
            ClearSelectionHighlights();

            hasClickSelection = true;
            clickSelectedSquare = square;

            // Highlight selected square + legal moves (without clearing move history)
            boardUI.OverrideSquareColour(square, BoardUI.HighlightType.Selected);
            boardUI.HighlightLegalMoves(board, square);
        }

        void ClearSelectionHighlights()
        {
            if (hasClickSelection)
            {
                // Remove previous selected square highlight by resetting to default
                boardUI.ResetSquareColour(clickSelectedSquare);

                // Clear all legal move highlights by regenerating legal moves and removing them
                MoveGenerator generator = new();
                var legalMoves = generator.GenerateMoves(board);
                foreach (var move in legalMoves)
                {
                    if (move.StartSquareIndex == clickSelectedSquare)
                    {
                        boardUI.ResetSquareColour(move.TargetSquareIndex);
                    }
                }
            }
        }

        bool TryMakeMove(int startSquare, int targetSquare)
        {
            bool isLegal = false;
            Move move = Move.NullMove;

            MoveGenerator generator = new();
            var legalMoves = generator.GenerateMoves(board);
            foreach (var legalMove in legalMoves)
            {
                if (legalMove.StartSquareIndex == startSquare && legalMove.TargetSquareIndex == targetSquare)
                {
                    isLegal = true;
                    move = legalMove;
                    break;
                }
            }

            if (isLegal)
            {
                isTurnToMove = false;
                ClearAllStates();
                MoveChosen?.Invoke(move);
                return true;
            }
            return false;
        }

        static bool LeftMousePressedThisFrame() => Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT);
        static bool LeftMouseReleasedThisFrame() => Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_LEFT);
        static bool RightMousePressedThisFrame() => Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_RIGHT);

    }
}