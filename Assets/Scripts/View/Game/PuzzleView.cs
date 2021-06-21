using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using UnityEngine;
using View.Control;
using View.Items;

namespace View.Game
{
    /// <summary>
    /// Contains functions to rotate/shake nodes and change their color. 
    /// </summary>
    public class PuzzleView : MonoBehaviour
    {
        private PuzzleScale _puzzleScale;
        private PuzzleState _puzzleState;
        private PuzzleInfo _puzzleInfo;
        private CursorSwipe _cursorSwipe;

        public event Action ViewUpdated;

        private void Awake()
        {
            _puzzleScale = GetComponent<PuzzleScale>();
            _puzzleState = GetComponent<PuzzleState>();
            _puzzleInfo = GetComponentInChildren<PuzzleInfo>();
            _cursorSwipe = GetComponentInChildren<CursorSwipe>();
        }

        public void Init(Point startNode, Point boardSize)
        {
            _puzzleScale.Init(startNode, boardSize);
            _puzzleInfo.Init();
            OnViewUpdated();
        }

        public void ResumeView()
        {
            OnViewUpdated();
        }

        public void Rotate(NodeView nodeView, ArcView arcView, Direction dir, bool pull)
        {
            arcView.transform.parent = nodeView.Rotor;
            Rotate(nodeView, dir, pull);
        }

        // TODO: make pull cleaner
        public void Rotate(NodeView nodeView, Direction dir, bool pull)
        {
            // Set all connecting arcs as the parent of this node
            // so that all arcs will rotate accordingly
            var arcViews = _puzzleState.GetArcs(nodeView.Position);

            // A node/arc that is parallel to the rotation (may not exist)
            NodeView stayNodeView = null;
            ArcView stayArcView = null;

            var stay = pull ? dir : dir.Opposite();
            foreach (var pair in arcViews) {
                if (pair.Key == stay) {
                    // This arc should not rotate with the node, do something else with it
                    // TODO: method to find node in a given direction
                    var stayNode = pair.Value.Arc.ParentNode.Equals(nodeView.Node) ? 
                        pair.Value.Arc.ConnectedNode : pair.Value.Arc.ParentNode;
                    stayNodeView = _puzzleState.PlayerNodes
                        .FirstOrDefault(node => node.Node.Equals(stayNode));
                    stayArcView = pair.Value;
                } else {
                    pair.Value.transform.parent = nodeView.Rotor;
                }
            }

            // Do a small rotate to the parallel node that stays put
            if (stayNodeView != null && !LeanTween.isTweening(stayNodeView.gameObject)) {
                stayArcView.transform.parent = stayNodeView.Rotor;
                stayNodeView.SlightRotate(dir.Opposite(), stayArcView.Arc.Length);
            }

            if (!pull) {
                // Push the newly connected node down
                var moveArc = arcViews[dir].Arc;
                var moveNode = moveArc.ParentNode.Equals(nodeView.Node) ?
                        moveArc.ConnectedNode : moveArc.ParentNode;
                var moveNodeView = _puzzleState.PlayerNodes
                    .FirstOrDefault(node => node.Node.Equals(moveNode));
                moveNodeView?.PushDown();

//                foreach (var node in _puzzleState.PlayerNodes) {
//                    node.PushDown();
//                }
            }

            // Finally, rotate the node!
            nodeView.Rotate(dir,  OnViewUpdated);
        }

        public void Shake(NodeView nodeView, Direction dir)
        {
            // Set all connecting arcs as the parent of this node
            // so that all arcs will rotate accordingly
            var arcViews = _puzzleState.GetArcs(nodeView.Position);
            foreach (var pair in arcViews) {
                pair.Value.transform.parent = nodeView.Rotor;
            }

            // Finally, rotate the node!
            nodeView.Shake(dir, OnViewUpdated);
        }

        public void MoveRotate(List<NodeView> nodeViews, ArcView arcView, Direction dir, Action onComplete)
        {
            arcView.MoveTo(nodeViews, () => {
                arcView.PushSound();
                Rotate(nodeViews[nodeViews.Count - 1], arcView, dir, false);
                onComplete();
            });
        }
        
        public void FloatIsland(bool pulled)
        {
            var dir = pulled ? Direction.Up : Direction.Down;
            
            foreach (var arc in _puzzleState.PlayerArcs
                .Where(arc => !arc.Equals(_puzzleState.PulledArcView))) {
                arc.Float(dir);
            }
        }

        public void Highlight(IEnumerable<NodeView> nodes, bool enable)
        {
            foreach (var nodeView in nodes) {
                nodeView.Highlight(enable);
            }
        }

        public void Highlight(IEnumerable<ArcView> arcs, bool enable)
        {
            foreach (var arcView in arcs)
            {
                arcView.Highlight(enable);
            }
        }

        public void Highlight(ArcView arcView, bool enable)
        {
            arcView.Highlight(enable);
        }
        
        public void Highlight(IEnumerable<FieldView> fields, bool enable)
        {
            foreach (var fieldView in fields) {
                fieldView.Highlight(enable);
            }
        }

        /// <summary>
        /// Shakes the entire game board
        /// </summary>
        public void Shake(Direction dir)
        {
            LeanTween.cancel(gameObject);

            var dirVector = dir.Vector();

            // TODO: make configurable
            const float shakeAmount = 0.025f;
            const float shakePeriodTime = 0.1f;
            
            var initPos = transform.localPosition;

            LeanTween.moveLocal(gameObject, initPos + dirVector * shakeAmount, shakePeriodTime)
                .setEase(LeanTweenType.easeInSine)
                .setOnComplete(() => {
                    LeanTween.moveLocal(gameObject, initPos, shakePeriodTime)
                        .setEase(LeanTweenType.easeOutSine);
                });
        }

        private void OnViewUpdated()
        {
            if (_puzzleState.IsTutorial) {
                var move = _puzzleState.TutorialMove;

                var pos = (Vector2) move.Point * _puzzleScale.Scaling;
                _cursorSwipe.Show(pos, move.Direction);
            } else {
                _cursorSwipe.Hide();
            }
            
            ViewUpdated?.Invoke();
        }

        public ArcView ConnectArcs(NodeView nodeView) {
            var connectedArcs = nodeView.Node.Connections
                .Where(field => field.HasArc)
                .Select(field => field.Arc)
                .Select(arc => _puzzleState.GetArcs(arc.Position)[arc.Direction]);

            ArcView lastArc = null;
            foreach (var arc in connectedArcs) {
                arc.transform.parent = nodeView.Rotor.transform;
                lastArc = arc;
            }

            return lastArc;
        }
    }
}
