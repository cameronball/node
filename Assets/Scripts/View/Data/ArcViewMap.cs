using System.Collections.Generic;
using Core.Data;
using View.Items;

namespace View.Data
{
    /// <summary>
    /// Maps point directions to their corresponding arc views.
    /// </summary>
    public class ArcViewMap
    {
        private readonly IDictionary<PointDir, ArcView> _arcMap = new Dictionary<PointDir, ArcView>();
        private readonly IDictionary<Point, IDictionary<Direction, ArcView>> _arcSet = new Dictionary<Point, IDictionary<Direction, ArcView>>(); 

        public IEnumerable<ArcView> Arcs => _arcMap.Values;

        public void Reset(ArcViewMap arcViewMap)
        {
            _arcMap.Clear();
            _arcSet.Clear();

            foreach (var pair in arcViewMap._arcMap) {
                _arcMap.Add(pair.Key, pair.Value);
            }

            foreach (var pair in arcViewMap._arcSet) {
                _arcSet.Add(pair.Key, pair.Value);
            }
        }

        public bool ContainsArc(Point pos, Direction dir)
        {
            return _arcMap.ContainsKey(new PointDir(pos, dir));
        }

        public IDictionary<Direction, ArcView> GetArcs(Point pos)
        {
            IDictionary<Direction, ArcView> arcViews;
            if (_arcSet.TryGetValue(pos, out arcViews)) {
                return arcViews;
            }

            arcViews = new Dictionary<Direction, ArcView>();
            _arcSet.Add(pos, arcViews);
            return arcViews;
        }

        public bool TryGetArc(Point pos, Direction dir, out ArcView arcView)
        {
            return _arcMap.TryGetValue(new PointDir(pos, dir), out arcView);
        }

        public void Add(Point pos, Direction dir, ArcView arcView)
        {
            _arcMap.Add(new PointDir(pos, dir), arcView);

            IDictionary<Direction, ArcView> arcViews;
            if (_arcSet.TryGetValue(pos, out arcViews)) {
                arcViews.Add(dir, arcView);
                return;
            }

            arcViews = new Dictionary<Direction, ArcView> { { dir, arcView} };
            _arcSet.Add(pos, arcViews);
        }

        public bool Remove(Point pos, Direction dir)
        {
            _arcMap.Remove(new PointDir(pos, dir));

            IDictionary<Direction, ArcView> arcViews;
            return _arcSet.TryGetValue(pos, out arcViews) ? 
                arcViews.Remove(dir) : 
                _arcSet.Remove(pos);
        }

        public void Clear()
        {
            _arcMap.Clear();
            _arcSet.Clear();
        }

        public ArcView this[PointDir pointDir] => _arcMap[pointDir];
    }
}
