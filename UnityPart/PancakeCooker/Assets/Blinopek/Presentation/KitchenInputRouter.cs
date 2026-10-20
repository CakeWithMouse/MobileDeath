using Blinopek.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    public sealed class KitchenInputRouter : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IBeginDragHandler, IEndDragHandler
    {
        public GameSystem System;
        public bool Pan;
        private const float FoldSwipeDistance = 24f;
        private bool painting, held;
        private Vector2 start, gestureStart;
        private PancakeState target;
        private PancakeState StationPancake => Pan ? System.Session.PanPancake : System.Session.PlatePancake;
        public void OnPointerDown(PointerEventData e)
        {
            if (!System.Capture(e.pointerId, Pan)) return;
            held = true; start = Coordinates(e.position); target = StationPancake;
            gestureStart = LocalPosition(e);
            var stage = target == null ? PancakeStage.Batter : target.Stage;
            painting = Pan ? System.BatterSelected && stage == PancakeStage.Batter
                : !System.BatterSelected && stage == PancakeStage.Plate && System.SelectedIngredient < 3;
            if (painting) {
                if (!System.Command(ActionKind.BeginStroke, Pan ? -1 : System.SelectedIngredient, (int)start.x, (int)start.y)) { Cancel(); return; }
                target = StationPancake;
            }
        }
        public void OnDrag(PointerEventData e)
        {
            if (!held || !System.OwnsPointer(e.pointerId)) return;
            if (target != null && StationPancake != target) { Cancel(); return; }
            var p = Coordinates(e.position);
            if (painting) System.Command(ActionKind.Stroke, (int)p.x, (int)p.y);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (!held || !System.OwnsPointer(e.pointerId)) return;
            var p = Coordinates(e.position);
            if (painting) System.Command(ActionKind.EndStroke);
            else if (target == StationPancake && target != null)
            {
                if (!Pan && target.Stage == PancakeStage.Folding) FoldGesture(e);
                else if (System.OverTrash(e.position)) System.Command(ActionKind.Discard, target.Id);
                else if (Pan && System.OverPlate(e.position)) System.Command(ActionKind.MoveToPlate);
                else if (Pan && target.Stage == PancakeStage.SideA && (p - start).sqrMagnitude < 2500) System.Command(ActionKind.Flip);
            }
            Cancel();
        }
        public void OnBeginDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }
        private Vector2 LocalPosition(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out var point);
            return point;
        }
        private void FoldGesture(PointerEventData e)
        {
            // A short swipe anywhere on the pancake selects a direction. Measure in canvas
            // units so both axes feel the same, and keep movement beyond the plate's rim.
            var delta = LocalPosition(e) - gestureStart;
            if (Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) < FoldSwipeDistance) return;
            bool horizontal = Mathf.Abs(delta.x) > Mathf.Abs(delta.y);
            System.Command(ActionKind.FoldGesture, horizontal ? (delta.x > 0 ? 400 : -400) : 0,
                horizontal ? 0 : (delta.y > 0 ? 400 : -400));
        }
        private Vector2 Coordinates(Vector2 screen)
        {
            var rect = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, null, out var point);
            // Round back to the simulation grid; truncation can push a point just inside the rim outside the circle.
            return new Vector2(Mathf.RoundToInt(Mathf.Clamp((point.x / rect.rect.width + .5f) * 1000, 0, 1000)), Mathf.RoundToInt(Mathf.Clamp((point.y / rect.rect.height + .5f) * 1000, 0, 1000)));
        }
        private void Cancel() { held = false; painting = false; System?.ReleasePointer(); }
        private void OnDisable() { if (held) { System?.Command(ActionKind.EndStroke); Cancel(); } }
    }

    public sealed class TrayServeDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IBeginDragHandler, IEndDragHandler
    {
        public GameSystem System;
        public bool IsDragging { get; private set; }
        private const float ForwardDistance = 70f;
        private Vector2 start, home, direction;
        private int orderId;
        private RectTransform Tray => (RectTransform)transform;
        private Vector2 Point(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, e.position, e.pressEventCamera, out var point);
            return point;
        }
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !System.CanServeTray || !System.Capture(e.pointerId, true)) return;
            IsDragging = true; start = Point(e); home = Tray.anchoredPosition; orderId = System.Session.Order.Id;
            direction = System.TrayServeDirection;
        }
        public void OnDrag(PointerEventData e)
        {
            if (!IsDragging || !System.OwnsPointer(e.pointerId)) return;
            if (!System.CanServeTray || System.Session.Order.Id != orderId) { Cancel(); return; }
            var delta = Point(e) - start;
            var sideways = new Vector2(-direction.y, direction.x);
            Tray.anchoredPosition = home + direction * Mathf.Clamp(Vector2.Dot(delta, direction), 0, 320)
                + sideways * Mathf.Clamp(Vector2.Dot(delta, sideways), -40, 40);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (!IsDragging || !System.OwnsPointer(e.pointerId)) return;
            var delta = Point(e) - start;
            float forward = Vector2.Dot(delta, direction);
            float sideways = Mathf.Abs(Vector2.Dot(delta, new Vector2(-direction.y, direction.x)));
            if (System.Session.Order.Id == orderId && System.CanServeTray && forward >= ForwardDistance && forward > sideways)
                System.Command(ActionKind.ServeOrder);
            Cancel();
        }
        public void OnBeginDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }
        private void Cancel() { Tray.anchoredPosition = home; IsDragging = false; System.ReleasePointer(); }
        private void OnDisable() { if (IsDragging) Cancel(); }
        private void OnApplicationFocus(bool focused) { if (!focused && IsDragging) Cancel(); }
        private void OnApplicationPause(bool paused) { if (paused && IsDragging) Cancel(); }
    }

    public sealed class ReadyPancakeDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerClickHandler, IBeginDragHandler, IEndDragHandler
    {
        public GameSystem System;
        public PancakeState Pancake;
        private PancakeState target;
        private bool held, dragged;
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || Pancake == null || System.Session.FindTrayPancake(Pancake.Id) != Pancake || !System.Capture(e.pointerId, true, true)) return;
            held = true; dragged = false; target = Pancake; System.SelectReadyPancake(target.Id);
        }
        public void OnDrag(PointerEventData e)
        {
            if (!held || !System.OwnsPointer(e.pointerId)) return;
            if (System.Session.FindTrayPancake(target.Id) != target) { Cancel(); return; }
            dragged = true;
            System.MoveReadyGhost(target, e.position);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (!held || !System.OwnsPointer(e.pointerId)) return;
            if (System.OverTrash(e.position)) System.Command(ActionKind.Discard, target.Id);
            Cancel();
        }
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && !dragged && !e.dragging &&
                Pancake != null && System.Session.FindTrayPancake(Pancake.Id) == Pancake) System.BeginServing();
        }
        public void OnBeginDrag(PointerEventData e) { if (held && System.OwnsPointer(e.pointerId)) dragged = true; }
        public void OnEndDrag(PointerEventData e) { }
        private void Cancel() { held = false; target = null; System.HideReadyGhost(); System.ReleasePointer(); }
        private void OnDisable() { if (held) Cancel(); }
    }

    public sealed class IngredientDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IBeginDragHandler, IEndDragHandler
    {
        public GameSystem System;
        public int Ingredient;
        private bool held;
        private PancakeState target;
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || System.Session == null || !System.Session.CanCook ||
                (Ingredient >= 0 && !System.Session.StartProfile.Owns((Ingredient)Ingredient))) return;
            // Selection remains available while the pan is frying or the plate is folding.
            if (!System.Capture(e.pointerId, true)) return;
            held = true; target = System.Session.PlatePancake;
            if (Ingredient < 0) System.SelectBatter(); else System.SelectIngredient(Ingredient);
        }
        public void OnDrag(PointerEventData e)
        {
            if (!held || !System.OwnsPointer(e.pointerId)) return;
            if (!SamePancake()) { Cancel(); return; }
            if (Ingredient >= 3 && !System.Session.StartProfile.HelperEnabled((Ingredient)Ingredient)) System.MovePortionGhost(Ingredient, e.position);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (!held || !System.OwnsPointer(e.pointerId)) return;
            if (SamePancake() && Ingredient >= 3 && System.OverPlate(e.position) && !System.Session.StartProfile.HelperEnabled((Ingredient)Ingredient))
            { var p = System.PlateCoordinates(e.position); System.Command(ActionKind.Portion, Ingredient, Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y)); }
            Cancel();
        }
        private bool SamePancake() => target != null && System.Session.PlatePancake == target && target.Stage == PancakeStage.Plate;
        private void Cancel() { held = false; target = null; System.ReleasePointer(); System.HidePortionGhost(); }
        public void OnBeginDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }
        private void OnDisable() { if (held) Cancel(); }
    }
}
