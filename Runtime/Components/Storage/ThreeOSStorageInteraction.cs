using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ThreeOS
{
    internal sealed class ThreeOSStorageInteraction
    {
        private readonly ThreeOSStorageController _host;
        internal ThreeOSStorageInteraction(ThreeOSStorageController host) { _host = host; }

        private ThreeOSBridge _bridge => _host.Bridge;
        private ThreeOSInputRouter _input => _host.Input;
        private ThreeOSTopologyController _topology => _host.Topology;
        private Dictionary<ulong, ThreeOSGlyphView> _views => _host.Glyphs.Views;
        private Dictionary<ulong, ThreeOSGlyphView> _ghostViews => _host.Glyphs.GhostViews;
        private Dictionary<ulong, ThreeOSExpandedBoxUi> _expandedBoxes => _host.ExpandedBoxes;
        private HashSet<ulong> _animLockedIds => _host.AnimLockedIds;
        private ThreeOSArrowVisual _insertArrow { get => _host.InsertArrow; set => _host.InsertArrow = value; }
        private bool _triggerWasDown { get => _host.TriggerWasDown; set => _host.TriggerWasDown = value; }
        private float _triggerDownTime { get => _host.TriggerDownTime; set => _host.TriggerDownTime = value; }
        private bool _triggerDragActive { get => _host.TriggerDragActive; set => _host.TriggerDragActive = value; }
        private bool _triggerGestureConsumed { get => _host.TriggerGestureConsumed; set => _host.TriggerGestureConsumed = value; }
        private Vector3 _triggerDownCursorXr { get => _host.TriggerDownCursorXr; set => _host.TriggerDownCursorXr = value; }
        private bool _triggerDownCursorValid { get => _host.TriggerDownCursorValid; set => _host.TriggerDownCursorValid = value; }
        private int _selectHand { get => _host.SelectHandField; set => _host.SelectHandField = value; }
        private bool _possessing { get => _host.Possessing; set => _host.Possessing = value; }
        private ulong _possessedId { get => _host.PossessedId; set => _host.PossessedId = value; }
        private bool _ghostDragging { get => _host.GhostDragging; set => _host.GhostDragging = value; }
        private bool _insertAnimBusy => _host.InsertAnimBusy;
        private bool _boxAnimBusy => _host.BoxAnimBusy;
        private ulong _lastShortTapId { get => _host.LastShortTapId; set => _host.LastShortTapId = value; }
        private float _lastShortTapTime { get => _host.LastShortTapTime; set => _host.LastShortTapTime = value; }
        private string _status { get => _host.StatusField; set => _host.StatusField = value; }
        private string _selectionLabel { get => _host.SelectionLabelField; set => _host.SelectionLabelField = value; }
        private ulong _xHoverBoxId { get => _host.XHoverBoxId; set => _host.XHoverBoxId = value; }
        private bool _leftTriggerWasDown { get => _host.LeftTriggerWasDown; set => _host.LeftTriggerWasDown = value; }
        private bool _rightTriggerWasDown { get => _host.RightTriggerWasDown; set => _host.RightTriggerWasDown = value; }
        private float PossessDistance => _host.PossessDistance;
        private float TriggerHoldDragSeconds => _host.TriggerHoldDragSeconds;
        private float SummonZoneHoldDragSeconds => _host.SummonZoneHoldDragSeconds;
        private float TriggerAxisThreshold => _host.TriggerAxisThreshold;
        private float BoxDoubleTapSeconds => _host.BoxDoubleTapSeconds;
        private Coroutine StartCoroutine(IEnumerator rut) => _host.StartCoroutinePublic(rut);
        private void Destroy(UnityEngine.Object o) { if (o != null) UnityEngine.Object.Destroy(o); }
        private void DestroyGlyphView(ThreeOSGlyphView view) => _host.Glyphs.DestroyGlyphView(view);

        internal void HandleTriggerSelectAndDrag()
        {
            if (_topology != null && (_topology.DomeActive || _topology.LatticeActive))
            {
                if (_triggerDragActive && (_possessing || _ghostDragging))
                {
                    ReleaseDragFromTrigger();
                }

                _triggerWasDown = false;
                _leftTriggerWasDown = false;
                _rightTriggerWasDown = false;
                _triggerDragActive = false;
                _triggerGestureConsumed = false;
                _selectHand = -1;
                return;
            }

            var frame = _input.LastFrame;
            var leftDown = frame.leftTrigger >= TriggerAxisThreshold;
            var rightDown = frame.rightTrigger >= TriggerAxisThreshold;
            var triggerDown = leftDown || rightDown;

            // Per-hand rising edge: X dismiss or scissors cut, independently per hand.
            var leftChrome = false;
            var rightChrome = false;
            if (leftDown && !_leftTriggerWasDown)
            {
                leftChrome = _host.Dismiss.TryDismissHoveredPortal(0) || _host.Dismiss.TryCutHoveredPortal(0);
            }

            if (rightDown && !_rightTriggerWasDown)
            {
                rightChrome = _host.Dismiss.TryDismissHoveredPortal(1) || _host.Dismiss.TryCutHoveredPortal(1);
            }

            _leftTriggerWasDown = leftDown;
            _rightTriggerWasDown = rightDown;

            if (triggerDown && !_triggerWasDown)
            {
                if (leftDown && !rightDown)
                {
                    _selectHand = 0;
                }
                else if (rightDown && !leftDown)
                {
                    _selectHand = 1;
                }
                else
                {
                    _selectHand = frame.leftTrigger >= frame.rightTrigger ? 0 : 1;
                }

                _triggerDownTime = Time.unscaledTime;
                _triggerDragActive = false;
                _triggerGestureConsumed = false;
                _triggerDownCursorXr = SelectHandCursorOpenXr();
                _triggerDownCursorValid = true;
                var chromeThisHand = (_selectHand == 0 && leftChrome) ||
                                     (_selectHand == 1 && rightChrome);
                if (chromeThisHand || _host.Dismiss.TryBeginCloseOnXHover())
                {
                    _triggerGestureConsumed = true;
                }
            }

            var aimUnity = SelectAimUnity(_selectHand, out var rayDir);

            if (triggerDown && _triggerWasDown && !_triggerDragActive && !_possessing &&
                !_ghostDragging && !_triggerGestureConsumed)
            {
                _bridge.TryGetSelection(out var selForHold);
                var cursorXr = SelectHandCursorOpenXr();
                var homePortal = selForHold != 0 ? _bridge.PortalForGhost(selForHold) : 0UL;
                var portalAtCursor = _bridge.PortalAtNearPoint(cursorXr);
                // Summon zone: selected ghost + cursor outside its home near BB.
                // Use a longer hold so a normal pinch releases as summon, not far-drag.
                var summonZone = homePortal != 0 && portalAtCursor != homePortal;
                float holdSec;
                if (summonZone)
                {
                    holdSec = Mathf.Max(TriggerHoldDragSeconds, SummonZoneHoldDragSeconds);
                }
                else if (selForHold != 0)
                {
                    // Inside home BB / near-real: still prefer drag over re-select.
                    holdSec = Mathf.Min(TriggerHoldDragSeconds, 0.14f);
                }
                else
                {
                    holdSec = TriggerHoldDragSeconds;
                }

                if (Time.unscaledTime - _triggerDownTime >= holdSec)
                {
                    // Palm-down: yellow voodoo always wins — never start selection /
                    // possess / ghost-drag (that would mark the hand busy and cancel
                    // VoodooSnap in the kernel). Sideways / Up keep telekinesis paths.
                    if (SelectHandPalmMode() == PalmMode.Down)
                    {
                        // Leave drag inactive so release stays a no-op select tap.
                    }
                    else
                    {
                        var selectedId = selForHold;
                        // Hover glyph/ghost under the cursor wins over a prior selection so
                        // direct-drag on B works while A is selected. No hit + selection:
                        // drag selection — velocity / possess / ghost-scale as before.
                        ulong dragId = 0;
                        if (TryHitTestYawAligned(aimUnity.position, rayDir, out var hitId) &&
                            TryFindItem(hitId, out var hitItem) &&
                            (hitItem.kind == 0 || hitItem.kind == 1))
                        {
                            if (hitId != selectedId && _bridge.SetSelection(hitId))
                            {
                                _selectionLabel = hitItem.Name;
                                _status = $"Sel:{_selectionLabel}";
                            }

                            dragId = hitId;
                        }
                        else if (selectedId != 0)
                        {
                            dragId = selectedId;
                        }

                        if (dragId != 0)
                        {
                            _triggerDragActive = true;
                            TryBeginSelectedDrag(dragId, aimUnity.position);
                        }
                    }
                }
            }

            if (!triggerDown && _triggerWasDown)
            {
                if (_triggerGestureConsumed)
                {
                    // X close (or other consumed press) — do not select/drag.
                }
                else if (_triggerDragActive || _possessing || _ghostDragging)
                {
                    ReleaseDragFromTrigger();
                }
                else if (SelectHandPalmMode() == PalmMode.Down &&
                         Time.unscaledTime - _triggerDownTime >= TriggerHoldDragSeconds)
                {
                    // Held palm-down for voodoo — do not treat release as a select tap.
                }
                else if (_triggerDownCursorValid &&
                         Vector3.Distance(SelectHandCursorOpenXr(), _triggerDownCursorXr) >
                         SummonSwipeCancelMeters())
                {
                    // Moved while held but never crossed drag threshold — abort, do not summon.
                    _status = "drag cancelled";
                }
                else
                {
                    // Short press: double-tap Box opens; else select / summon / deselect.
                    HandleShortTriggerTap(aimUnity.position, rayDir);
                }

                _triggerDragActive = false;
                _triggerGestureConsumed = false;
                _triggerDownCursorValid = false;
                _selectHand = -1;
            }

            _triggerWasDown = triggerDown;
        }

        /// <summary>
        /// Cursor travel that cancels a short-release summon. Wider in summon zone so
        /// Quest hand jitter during a deliberate pinch does not abort.
        /// </summary>
        internal float SummonSwipeCancelMeters()
        {
            if (!_bridge.TryGetSelection(out var sel) || sel == 0)
            {
                return 0.03f;
            }

            var home = _bridge.PortalForGhost(sel);
            if (home == 0)
            {
                return 0.03f;
            }

            var at = _bridge.PortalAtNearPoint(SelectHandCursorOpenXr());
            return at != home ? 0.08f : 0.03f;
        }

        internal void HandleShortTriggerTap(Vector3 rayOrigin, Vector3 rayDir)
        {
            if (_boxAnimBusy || _insertAnimBusy)
            {
                return;
            }

            _bridge.TryGetSelection(out var selectedId);
            var hitSomething = TryHitTestYawAligned(rayOrigin, rayDir, out var hitId);
            var pinchXr = SelectHandCursorOpenXr();
            var homePortal = selectedId != 0 ? _bridge.PortalForGhost(selectedId) : 0UL;
            var portalAtPinch = _bridge.PortalAtNearPoint(pinchXr);
            var hitPortal = hitSomething ? _bridge.PortalForGhost(hitId) : 0UL;

            // Task 6.6: armed scissors cut — cancel inside source BB, else paste.
            var cutPortal = _bridge.VoodooCutPortal;
            if (cutPortal != 0)
            {
                if (portalAtPinch == cutPortal)
                {
                    _bridge.VoodooCutCancel();
                    _status = "cut cancelled";
                    _lastShortTapId = 0;
                    return;
                }

                if (hitSomething && TryFindItem(hitId, out var pasteBox) && pasteBox.kind == 1 &&
                    hitPortal != cutPortal)
                {
                    if (_bridge.VoodooCutPasteInto(hitId))
                    {
                        _bridge.ClearSelection();
                        _selectionLabel = "none";
                        _status = $"cut paste → {pasteBox.Name}";
                        _lastShortTapId = 0;
                        _host.Glyphs.ClearGhostViews();
                    }
                    else
                    {
                        _status = $"cut paste-into failed: {_bridge.LastError}";
                    }

                    return;
                }

                if (_bridge.VoodooCutPasteAt(pinchXr))
                {
                    _bridge.ClearSelection();
                    _selectionLabel = "none";
                    _status = portalAtPinch != 0 ? "cut paste → portal" : "cut paste → near field";
                    _lastShortTapId = 0;
                    _host.Glyphs.ClearGhostViews();
                }
                else
                {
                    _status = $"cut paste failed: {_bridge.LastError}";
                }

                return;
            }

            // Select-drop onto a Box (same or other near BB): insert, don't retarget selection.
            // Near-field matrix without portals still uses tap-to-select (no auto-insert).
            if (selectedId != 0 && hitSomething &&
                TryFindItem(hitId, out var boxHit) && boxHit.kind == 1 && hitId != selectedId &&
                (homePortal != 0 || hitPortal != 0 || portalAtPinch != 0))
            {
                if (_bridge.InsertInto(selectedId, hitId))
                {
                    _bridge.ClearSelection();
                    _selectionLabel = "none";
                    _status = $"insert → {boxHit.Name}";
                    _lastShortTapId = 0;
                    _host.Glyphs.RemoveGhostViewsForEntity(selectedId);
                }
                else
                {
                    _status = $"insert failed: {_bridge.LastError}";
                }

                return;
            }

            // Task 6.5: empty pinch with selection → deselect / summon / insert.
            if (selectedId != 0 && !hitSomething)
            {
                // Selected ghost + empty inside home BB → deselect.
                if (homePortal != 0 && portalAtPinch == homePortal)
                {
                    _bridge.ClearSelection();
                    _selectionLabel = "none";
                    _status = "Sel:none";
                    _lastShortTapId = 0;
                    return;
                }

                // Near-field selection (post-summon or matrix real): open-air empty
                // pinch deselects. Do not re-summon to the pinch — that left selection
                // permanently sticky because every miss called summon again.
                if (homePortal == 0 && portalAtPinch == 0)
                {
                    _bridge.ClearSelection();
                    _selectionLabel = "none";
                    _status = "Sel:none";
                    _lastShortTapId = 0;
                    return;
                }

                // Ghost outside / cross-portal, or near-real into a BB → summon/insert + lerp.
                if (_bridge.VoodooSummon(selectedId, pinchXr))
                {
                    if (homePortal == 0 && portalAtPinch != 0)
                    {
                        _status = "insert → far (lerp)";
                    }
                    else if (portalAtPinch != 0 && portalAtPinch != homePortal)
                    {
                        _status = "summon cross-portal (lerp)";
                    }
                    else
                    {
                        _status = "summon near-field (lerp)";
                    }

                    _lastShortTapId = 0;
                    return;
                }
            }

            if (!hitSomething)
            {
                _lastShortTapId = 0;
                ApplySelectAtAim(rayOrigin, rayDir);
                return;
            }

            var now = Time.unscaledTime;
            var isBox = TryFindItem(hitId, out var hitItem) && hitItem.kind == 1;
            var doubleTap = isBox && hitId == _lastShortTapId &&
                            (now - _lastShortTapTime) <= BoxDoubleTapSeconds &&
                            !_expandedBoxes.ContainsKey(hitId);

            if (doubleTap)
            {
                _lastShortTapId = 0;
                _lastShortTapTime = -10f;
                StartCoroutine(_host.Expanded.OpenBoxSequence(hitId));
                return;
            }

            _lastShortTapId = hitId;
            _lastShortTapTime = now;
            ApplySelectAtAim(rayOrigin, rayDir);
        }

        internal void RemoveGhostViewsForEntity(ulong entityId)
        {
            if (entityId == 0 || _ghostViews.Count == 0)
            {
                return;
            }

            var dead = new List<ulong>();
            foreach (var kv in _ghostViews)
            {
                if (kv.Value.EntityId == entityId)
                {
                    dead.Add(kv.Key);
                }
            }

            foreach (var key in dead)
            {
                DestroyGlyphView(_ghostViews[key]);
                _ghostViews.Remove(key);
            }
        }

        internal void ApplySelectAtAim(Vector3 rayOrigin, Vector3 rayDir)
        {
            if (TryHitTestYawAligned(rayOrigin, rayDir, out var hitId))
            {
                if (_bridge.SetSelection(hitId))
                {
                    _selectionLabel = TryFindItem(hitId, out var item) ? item.Name : hitId.ToString();
                    _status = $"Sel:{_selectionLabel}";
                }
                else
                {
                    _status = $"select failed: {_bridge.LastError}";
                }
            }
            else
            {
                _bridge.ClearSelection();
                _selectionLabel = "none";
                _status = "Sel:none";
            }
        }

        internal void ReleasePossessedFromTrigger()
        {
            var hadSnap = _bridge.TryGetSnap(out var snap) && snap.pending != 0;
            if (!_bridge.ReleasePossessed())
            {
                // Do not retry TryCommitInsert — kernel owns snap commit/clear on
                // release; host retries caused multi-box FS thrash / frame spikes.
                _status = hadSnap
                    ? $"insert failed: {_bridge.LastError}"
                    : $"released (coast): {_bridge.LastError}";
            }
            else
            {
                _status = hadSnap ? "insert — waiting FS" : "released — coasting";
            }

            _possessing = false;
            _possessedId = 0;
            _triggerDragActive = false;
            _host.MarkStorageVisualsDirty();
        }

        internal void ReleaseDragFromTrigger()
        {
            if (_ghostDragging && _selectHand >= 0)
            {
                var hadSnap = _bridge.TryGetSnap(out var snap) && snap.pending != 0;
                var pinchXr = SelectHandCursorOpenXr();
                var inside = _bridge.PointInAnyNearBb(pinchXr);
                var ok = _bridge.GhostDragEnd((uint)_selectHand, inside);
                if (hadSnap)
                {
                    // Kernel re-validates near snap range on end; do not retry commit
                    // (avoids double-insert / multi-box FS thrash and frame spikes).
                    _status = ok
                        ? "ghost insert — waiting FS"
                        : $"ghost snap released: {_bridge.LastError}";
                }
                else
                {
                    _status = inside ? "ghost drag end (inside)" : "ghost drag end";
                }

                _ghostDragging = false;
                _triggerDragActive = false;
                _host.MarkStorageVisualsDirty();
                return;
            }

            if (_possessing)
            {
                ReleasePossessedFromTrigger();
            }
        }

        internal Vector3 SelectHandCursorOpenXr()
        {
            if (_input != null)
            {
                if (_selectHand == 0 && _input.LastLeftCursor.active != 0)
                {
                    var p = _input.LastLeftCursor.position;
                    return new Vector3(p.x, p.y, p.z);
                }

                if (_selectHand == 1 && _input.LastRightCursor.active != 0)
                {
                    var p = _input.LastRightCursor.position;
                    return new Vector3(p.x, p.y, p.z);
                }
            }

            var aim = SelectAimUnity(_selectHand, out _);
            return InteropStructs.UnityToOpenXrPosition(aim.position);
        }

        internal void TryBeginSelectedDrag(ulong selectedId, Vector3 aimUnity)
        {
            if (_selectHand < 0)
            {
                return;
            }

            var cursorXr = SelectHandCursorOpenXr();
            var homePortal = _bridge.PortalForGhost(selectedId);
            var portalAtCursor = _bridge.PortalAtNearPoint(cursorXr);

            // Committed ghost inside home BB → ghost-scale drag.
            // Near-field real (no ghost) → audition drag (kernel handles enter/exit BB).
            // Committed ghost outside home BB → far telekinesis of the real.
            if (homePortal != 0 && portalAtCursor == homePortal)
            {
                if (_bridge.GhostDragBegin((uint)_selectHand, selectedId))
                {
                    _ghostDragging = true;
                    _possessedId = selectedId;
                    _status = "ghost drag — blue stick";
                    return;
                }
            }

            if (homePortal == 0)
            {
                if (_bridge.GhostDragBegin((uint)_selectHand, selectedId))
                {
                    _ghostDragging = true;
                    _possessedId = selectedId;
                    // Kernel picks direct (cursor-in-glyph) vs velocity stick for near-field.
                    _status = portalAtCursor != 0
                        ? "audition insert — ghost in BB"
                        : "near drag — direct if in glyph, else stick";
                    return;
                }
            }

            TryPossessNearest(aimUnity);
        }

        internal bool PossessForSelectHand(ulong entityId, ThreeOSPose objectPose)
        {
            if (_selectHand >= 0)
            {
                return _bridge.PossessWithHand(entityId, objectPose, _selectHand);
            }

            return _bridge.Possess(entityId, objectPose);
        }

        private PalmMode SelectHandPalmMode()
        {
            if (_input == null)
            {
                return PalmMode.Sideways;
            }

            if (_selectHand == 0 && _input.LastLeftCursor.active != 0)
            {
                return (PalmMode)_input.LastLeftCursor.palmMode;
            }

            if (_selectHand == 1 && _input.LastRightCursor.active != 0)
            {
                return (PalmMode)_input.LastRightCursor.palmMode;
            }

            return (PalmMode)_input.LastIntents.palmMode;
        }

        internal void TryPossessNearest(Vector3 aimUnity)
        {
            // Clear a stuck FS event so a prior failed insert cannot block the next grab/commit.
            if (!_insertAnimBusy &&
                _bridge.TryPollStorageEvent(out var stuck) &&
                stuck.awaitingAck != 0)
            {
                Debug.LogWarning("[3OS] clearing stuck storage event before possess");
                _bridge.AckStorageEvent(false);
            }

            _bridge.TryGetSelection(out var selectedId);

            // Selection-gated only — never possess a deselected / nearest glyph.
            if (selectedId == 0 ||
                !TryFindItem(selectedId, out var selectedItem) ||
                (selectedItem.kind != 0 && selectedItem.kind != 1))
            {
                _status = "grab requires selection";
                return;
            }

            // Prefer live view pose; fall back to kernel workspace pose when the far glyph
            // is missing/inactive so outside-home velocity still engages for ghosts.
            ThreeOSPose possessPose = selectedItem.pose;
            if (_views.TryGetValue(selectedId, out var selectedView) &&
                selectedView.Root != null && selectedView.Root.activeInHierarchy &&
                selectedView.Movable != null)
            {
                possessPose = selectedView.Movable.CurrentOpenXrPose();
            }

            if (PossessForSelectHand(selectedId, possessPose))
            {
                _possessing = true;
                _possessedId = selectedId;
                _status = selectedItem.kind == 1
                    ? "far possess folder — stick drag"
                    : "far possess — drag onto a folder";
            }
            else
            {
                _status = $"possess failed: {_bridge.LastError}";
            }
        }

        internal bool TryHitTestYawAligned(Vector3 rayOrigin, Vector3 rayDir, out ulong hitId)
        {
            hitId = 0;
            if (rayDir.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            rayDir.Normalize();
            var yaw = FloorYawRotation();
            var half = ThreeOSStorageController.GlyphSizeM * 0.5f;
            var bestT = float.PositiveInfinity;
            ulong bestId = 0;

            foreach (var kv in _views)
            {
                if (kv.Value.Root == null || !kv.Value.Root.activeInHierarchy)
                {
                    continue;
                }

                var center = kv.Value.Root.transform.position;
                if (!RayHitsYawAlignedAabb(rayOrigin, rayDir, center, yaw, half, out var t) || t < 0f ||
                    t >= bestT)
                {
                    continue;
                }

                bestT = t;
                bestId = kv.Key;
            }

            // Task 6.4/6.5: select ghosts → same far entity id (drag via ghost-scale path).
            if (_bridge != null && _bridge.IsLoaded)
            {
                var portals = _bridge.VoodooPortalCount;
                for (uint pi = 0; pi < portals; pi++)
                {
                    if (!_bridge.TryGetVoodooPortal(pi, out var portal))
                    {
                        continue;
                    }

                    var gc = _bridge.VoodooPortalGhostCount(portal.id);
                    for (uint gi = 0; gi < gc; gi++)
                    {
                        if (!_bridge.TryGetVoodooPortalGhost(portal.id, gi, out var ghost))
                        {
                            continue;
                        }

                        var center = InteropStructs.ToUnityPose(ghost.nearPose).position;
                        var gHalf = half * Mathf.Max(0.15f, ghost.scale);
                        if (!RayHitsYawAlignedAabb(rayOrigin, rayDir, center, yaw, gHalf, out var t) ||
                            t < 0f || t >= bestT)
                        {
                            continue;
                        }

                        bestT = t;
                        bestId = ghost.entityId;
                    }
                }
            }

            if (bestId == 0)
            {
                return false;
            }

            hitId = bestId;
            return true;
        }

        internal static bool RayHitsYawAlignedAabb(Vector3 origin, Vector3 dir, Vector3 center,
            Quaternion yaw, float half, out float tHit)
        {
            tHit = 0f;
            var inv = Quaternion.Inverse(yaw);
            var o = inv * (origin - center);
            var d = inv * dir;
            var tMin = float.NegativeInfinity;
            var tMax = float.PositiveInfinity;

            if (!Slab(o.x, d.x, -half, half, ref tMin, ref tMax) ||
                !Slab(o.y, d.y, -half, half, ref tMin, ref tMax) ||
                !Slab(o.z, d.z, -half, half, ref tMin, ref tMax))
            {
                return false;
            }

            tHit = tMin >= 0f ? tMin : tMax;
            return tHit >= 0f;
        }

        internal static bool Slab(float o, float d, float min, float max, ref float tMin, ref float tMax)
        {
            const float eps = 1e-8f;
            if (Mathf.Abs(d) < eps)
            {
                return o >= min && o <= max;
            }

            var invD = 1f / d;
            var t0 = (min - o) * invD;
            var t1 = (max - o) * invD;
            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            tMin = Mathf.Max(tMin, t0);
            tMax = Mathf.Min(tMax, t1);
            return tMin <= tMax;
        }

        internal static Quaternion FloorYawRotation()
        {
            var fwd = Vector3.forward;
            var cam = Camera.main;
            if (cam != null)
            {
                fwd = cam.transform.forward;
            }

            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
            {
                fwd = Vector3.forward;
            }

            return Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        internal bool TryFindItem(ulong id, out InteropWorkspaceItem item)
        {
            item = default;
            var count = _bridge.StorageItemCount();
            for (uint i = 0; i < count; i++)
            {
                if (_bridge.TryGetWorkspaceItem(i, out item) && item.entityId == id)
                {
                    return true;
                }
            }

            return false;
        }

        internal bool IsKinematicallyDriven(ulong entityId)
        {
            if (_possessing && _possessedId == entityId)
            {
                return true;
            }

            // Ghost-drag updates workspace each tick; SyncViews should still apply poses.
            if (_ghostDragging && _possessedId == entityId)
            {
                return false;
            }

            if (_bridge.TryGetKineticState(out var ks) && ks.entityId == entityId &&
                (ks.phase == (uint)KineticPhase.Possessed || ks.phase == (uint)KineticPhase.Coasting))
            {
                return true;
            }

            return false;
        }

        internal bool TryFindSnapGlyphRoot(ulong entityId, out Transform root)
        {
            root = null;
            // Prefer near ghosts so the drop affordance sits in the near-BB gap.
            foreach (var kv in _ghostViews)
            {
                if (kv.Value.EntityId == entityId && kv.Value.Root != null &&
                    kv.Value.Root.activeInHierarchy)
                {
                    root = kv.Value.Root.transform;
                    return true;
                }
            }

            if (_views.TryGetValue(entityId, out var view) && view.Root != null &&
                view.Root.activeInHierarchy)
            {
                root = view.Root.transform;
                return true;
            }

            return false;
        }

        internal void UpdateInsertArrow()
        {
            if (_insertArrow == null || !_bridge.TryGetSnap(out var snap) || snap.pending == 0)
            {
                _insertArrow?.Hide();
                return;
            }

            if (!TryFindSnapGlyphRoot(snap.objectId, out var objT) ||
                !TryFindSnapGlyphRoot(snap.boxId, out var boxT))
            {
                _insertArrow.Hide();
                return;
            }

            // Static down arrow parked in the hold gap (not the velocity stick).
            var objPos = objT.position;
            var boxPos = boxT.position;
            var x = (objPos.x + boxPos.x) * 0.5f;
            var z = (objPos.z + boxPos.z) * 0.5f;
            var gap = objPos.y - boxPos.y;
            Vector3 shaftStart;
            Vector3 tip;
            if (gap > 0.03f)
            {
                shaftStart = new Vector3(x, boxPos.y + gap * 0.72f, z);
                tip = new Vector3(x, boxPos.y + gap * 0.28f, z);
            }
            else
            {
                // Degenerate / overlapping: still show a compact drop cue.
                var midY = (objPos.y + boxPos.y) * 0.5f;
                shaftStart = new Vector3(x, midY + 0.02f, z);
                tip = new Vector3(x, midY - 0.02f, z);
            }

            var nearSnap = IsNearFieldSnapGlyph(snap.objectId) || IsNearFieldSnapGlyph(snap.boxId);
            var shaftColor = nearSnap
                ? new Color(0.35f, 0.75f, 1f, 0.9f)
                : new Color(1f, 0.85f, 0.2f, 0.9f);
            _insertArrow.Set(shaftStart, tip, shaftColor);
        }

        internal bool IsNearFieldSnapGlyph(ulong entityId)
        {
            foreach (var kv in _ghostViews)
            {
                if (kv.Value.EntityId == entityId && kv.Value.Root != null &&
                    kv.Value.Root.activeInHierarchy)
                {
                    return true;
                }
            }

            return false;
        }
        internal static ThreeOSPose AimPose(InteropInputFrame frame)
        {
            if ((frame.trackingFlags & (uint)TrackingFlags.RightAim) != 0)
            {
                return frame.rightAim;
            }

            if ((frame.trackingFlags & (uint)TrackingFlags.LeftAim) != 0)
            {
                return frame.leftAim;
            }

            return frame.head;
        }

        /// <summary>
        /// Prefer Phase 0.5 point-cursor probe for <paramref name="hand"/> (0=left, 1=right, -1=any);
        /// ray dir uses head forward when that cursor is active.
        /// </summary>
        internal Pose SelectAimUnity(int hand, out Vector3 rayDir)
        {
            var aim = _input != null
                ? _input.AimPoseForHand(hand)
                : AimPose(_input != null ? _input.LastFrame : default);
            var pose = InteropStructs.ToUnityPose(aim);
            var cursorActive = false;
            if (_input != null)
            {
                if (hand == 0)
                {
                    cursorActive = _input.LastLeftCursor.active != 0;
                }
                else if (hand == 1)
                {
                    cursorActive = _input.LastRightCursor.active != 0;
                }
                else
                {
                    cursorActive = _input.LastLeftCursor.active != 0 ||
                                   _input.LastRightCursor.active != 0;
                }
            }

            if (cursorActive && Camera.main != null)
            {
                rayDir = Camera.main.transform.forward;
            }
            else
            {
                rayDir = pose.rotation * Vector3.forward;
            }

            return pose;
        }

        internal Pose SelectAimUnity(out Vector3 rayDir) => SelectAimUnity(-1, out rayDir);

    }
}
