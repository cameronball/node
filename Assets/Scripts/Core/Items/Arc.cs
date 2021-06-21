using Core.Data;

namespace Core.Items
{
    /// <inheritdoc />
    /// <summary>
    /// An Arc represents a visible connection between two nodes, allowing a traversible
    /// path between them.
    /// </summary>
    public class Arc : IBoardItem
    {
        public Point Position => ParentNode.Position;
        public Point ConnectedPosition => ConnectedNode.Position;
        public bool IsEnabled => true;
        public int Length { get; }
        public Direction Direction => Field?.Direction ?? Direction.None;

        public Node ParentNode => Field.ParentNode;
        public Node ConnectedNode => Field.ConnectedNode;
        public Field Field { get; private set; }
        public Field PrevField { get; private set; }

        /// <summary>
        /// True if this arc is in its pulled state.
        /// </summary>
        public bool IsPulled => Field == null;

        public Arc(Field field)
        {
            Length = field.Length;
        }

        /// <summary>
        /// Disconnects this Arc from an existing field
        /// </summary>
        public void Pull()
        {
            Field?.DisconnectArc(this);
            PrevField = Field;
            Field = null;
        }

        /// <summary>
        /// Connects this Arc to the new field
        /// </summary>
        public void Push(Field field)
        {
            // Pull before pushing
            Pull();

            Field = field;
            field.ConnectArc(this);
        }

        public Node Root(Direction dir)
        {
            return Field.Root(dir);
        }

        public override string ToString()
        {
            return IsPulled ? $"PULLED [{Length}]" : Field.ToString();
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            var arc = obj as Arc;
            return arc != null && Equals(arc);
        }

        public override int GetHashCode()
        {
            return Field != null ? Field.GetHashCode() : 0;
        }

        public bool Equals(Arc other)
        {
            return IsPulled || Field.Equals(other.Field);
        }
    }
}
