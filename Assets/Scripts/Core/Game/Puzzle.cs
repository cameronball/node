using System.Collections.Generic;
using Core.Data;
using Core.Items;
using Core.Moves;

namespace Core.Game
{
    /// <summary>
    /// A Puzzle specifies the rules and methods for interacting 
    /// with a <seealso cref="GameBoard"/>.
    /// </summary>
    public class Puzzle
    {
        private readonly GameBoard _gameBoard;
        private readonly Player _player;
        private readonly List<IMove> _moveHistory = new List<IMove>();

        public Node StartNode => _gameBoard.StartNode;
        public Level Metadata => _gameBoard.Metadata;
        public bool Win => _player.Win;
        public Point BoardSize => _gameBoard.Size;
        public long NumMoves => _player.NumMoves;
        public long MovesBestScore => _player.MovesBestScore;

        public PlayerState PlayerState => _player.PlayerState;

        public Arc PulledArc { get; private set; }
        public Direction PulledDirection { get; private set; } = Direction.None;
        public bool IsPulled => PulledArc != null;

        public Puzzle(GameBoard gameBoard)
        {
            _gameBoard = gameBoard;
            _player = new Player(gameBoard);
        }

        /// <summary>
        /// Pulls the arc in the given direction. Return true if this operation is valid and was
        /// executed.
        /// </summary>
        public bool PullArc(Arc arc, Direction pullDir)
        {
            if (IsPulled || arc == null) {
                return false;
            }

            var move = new PullMove(_gameBoard, _player, arc, pullDir);
            var result = _player.PlayMove(move);
            if (result) {
                _moveHistory.Add(move);
                PlayerState.UpdatePush(arc);
            }

            PulledArc = result ? arc : PulledArc;
            PulledDirection = result ? pullDir : PulledDirection;
            return result;
        }

        /// <summary>
        /// Pulls the arc attached to the node in the given direction. Return true if this
        /// operation is valid and was executed.
        /// </summary>
        public bool PullArc(Point nodePos, Direction pullDir)
        {
            var arc = _gameBoard.GetArcAt(nodePos, pullDir.Opposite());
            return PullArc(arc, pullDir);
        }

        /// <summary>
        /// Push the arc into the given field. Return true if this
        /// operation is valid and was executed.
        /// </summary>
        public bool PushArc(Field field)
        {
            if (!IsPulled || field == null) {
                return false;
            }

            var move = new PushMove(_gameBoard, _player, PulledArc, field);
            var result = _player.PlayMove(move);

            if (result) {
                _moveHistory.Add(move);
                PlayerState.UpdatePush(null);
            }

            PulledArc = result ? null : PulledArc;
            PulledDirection = result ? Direction.None : PulledDirection;
            return result;
        }

        /// <summary>
        /// Push the arc at the position into the field at the given direction. Return true if this
        /// operation is valid and was executed.
        /// </summary>
        public bool PushArc(Point nodePos, Direction pushDir)
        {
            var field = _gameBoard.GetFieldAt(nodePos, pushDir);
            return PushArc(field);
        }
    }
}
