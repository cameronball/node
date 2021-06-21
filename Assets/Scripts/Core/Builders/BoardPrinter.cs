using System.Collections.Generic;
using System.Text;
using Core.Data;
using Core.Items;

namespace Core.Builders
{
    /// <summary>
    /// Prints the state of the board using ASCII text
    /// </summary>
    public static class BoardPrinter
    {
        private const char Node = 'O';
        private const char ArcH = '-';
        private const char ArcV = '|';
        private const char Empty = ' ';
        private const char PullField = '*';
        private const char PushField = '#';

        public static string GetBoard(Point size, IEnumerable<Node> nodes, IEnumerable<Arc> arcs,
            IEnumerable<Field> pullFields, IEnumerable<Field> pushFields)
        {
            // Represent the board as an array of chars
            var grid = new char[2*size.x + 1, 2*size.y + 1];
            Reset(grid);

            foreach (var node in nodes) {
                grid[2*node.Position.x, 2*node.Position.y] = Node;
            }

            foreach (var arc in arcs) {
                if (arc.IsPulled) {
                    continue;
                }

                if (arc.Direction.IsHorizontal()) {
                    for (var i = 1; i < 2*arc.Length; i++) {
                        grid[2*arc.Position.x + i, 2*arc.Position.y] = ArcH;
                    }
                } else {
                    for (var i = 1; i < 2*arc.Length; i++) {
                        grid[2*arc.Position.x, 2*arc.Position.y + i] = ArcV;
                    }
                }
            }

            foreach (var field in pullFields) {
                if (field.Direction.IsHorizontal()) {
                    for (var i = 1; i < 2*field.Length; i++) {
                        grid[2*field.Position.x + i, 2*field.Position.y] = PullField;
                    }
                } else {
                    for (var i = 1; i < 2*field.Length; i++) {
                        grid[2*field.Position.x, 2*field.Position.y + i] = PullField;
                    }
                }
            }

            foreach (var field in pushFields) {
                if (field.Direction.IsHorizontal()) {
                    for (var i = 1; i < 2*field.Length; i++) {
                        grid[2*field.Position.x + i, 2*field.Position.y] = PushField;
                    }
                } else {
                    for (var i = 1; i < 2*field.Length; i++) {
                        grid[2*field.Position.x, 2*field.Position.y + i] = PushField;
                    }
                }
            }

            return BuildCharGrid(Transpose(grid));
        }

        private static void Reset(char[,] grid)
        {
            for (var i = 0; i < grid.GetLength(0); i++) {
                for (var j = 0; j < grid.GetLength(1); j++) {
                    grid[i, j] = Empty;
                }
            }
        }

        private static char[,] Transpose(char[,] grid)
        {
            var newGrid = new char[grid.GetLength(1), grid.GetLength(0)];

            for (var i = 0; i < grid.GetLength(1); i++) {
                for (var j = 0; j < grid.GetLength(0); j++) {
                    newGrid[i, j] = grid[j, i];
                }
            }

            return newGrid;
        }

        private static string BuildCharGrid(char[,] grid)
        {
            var builder = new StringBuilder();

            for (var i = 0; i < grid.GetLength(0); i++) {
                for (var j = 0; j < grid.GetLength(1); j++) {
                    builder.Append(grid[i, j]);
                }
                builder.Append('\n');
            }

            return builder.ToString();
        }
    }
}
