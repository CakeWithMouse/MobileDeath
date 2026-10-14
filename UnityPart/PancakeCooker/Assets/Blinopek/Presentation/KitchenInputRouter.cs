using Blinopek.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    public sealed class KitchenInputRouter : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IBeginDragHandler, IEndDragHandler
    {
        public GameView View;
        public bool Pan;
        private const float FoldSwipeDistance = 24f;
        private bool painting, held;
        private Vector2 start, gestureStart;
        private PancakeState target;
        private PancakeState StationPancake => Pan ? View.Session.PanPancake : View.Session.PlatePancake;
        public void OnPointerDown(PointerEventData e)
        {
            if (!View.Capture(e.pointerId, Pan)) return;
            held = true; start = Coordinates(e.position); target = StationPancake;
            gestureStart = LocalPosition(e);
            var stage = target == null ? PancakeStage.Batter : target.Stage;
            painting = Pan ? stage == PancakeStage.Batter : stage == PancakeStage.Plate && View.SelectedIngredient < 3;
            if (painting) {
                if (!View.Command(ActionKind.BeginStroke, Pan ? -1 : View.SelectedIngredient, (int)start.x, (int)start.y)) { Cancel(); return; }
                target = StationPancake;
            }
        }
        public void OnDrag(PointerEventData e)
        {
            if (!held || !View.OwnsPointer(e.pointerId)) return;
            if (target != null && StationPancake != target) { Cancel(); return; }
            var p = Coordinates(e.position);
            if (painting) View.Command(ActionKind.Stroke, (int)p.x, (int)p.y);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (!held || !View.OwnsPointer(e.pointerId)) return;
            var p = Coordinates(e.position);
            if (painting) View.Command(ActionKind.EndStroke);
            else if (target == StationPancake && target != null)
            {
                if (!Pan && target.Stage == PancakeStage.Folding) FoldGesture(e);
                else if (View.OverTrash(e.position)) View.Command(ActionKind.Discard, target.Id);
                else if (Pan && View.OverPlate(e.position)) View.Command(ActionKind.MoveToPlate);
                else if (Pan && target.Stage == PancakeStage.SideA && (p - start).sqrMagnitude < 2500) View.Command(ActionKind.Flip);
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
            View.Command(ActionKind.FoldGesture, horizontal ? (delta.x > 0 ? 400 : -400) : 0,
                horizontal ? 0 : (delta.y > 0 ? 400 : -400));
        }
        private Vector2 Coordinates(Vector2 screen)
        {
            var rect = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, null, out var point);
            // Round back to the simulation grid; truncation can push a point just inside the rim outside the circle.
            return new Vector2(Mathf.RoundToInt(Mathf.Clamp((point.x / rect.rect.width + .5f) * 1000, 0, 1000)), Mathf.RoundToInt(Mathf.Clamp((point.y / rect.rect.height + .5f) * 1000, 0, 1000)));
        }
        private void Cancel() { held = false; painting = false; View?.ReleasePointer(); }
        private void OnDisable() { if (held) { View?.Command(ActionKind.EndStroke); Cancel(); } }
    }

    public sealed class TrayServeDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IBeginDragHandler, IEndDragHandler
    {
        public GameView View;
        public bool IsDragging { get; private set; }
        private const float ForwardDistance = 70f;
        private Vector2 start, home;
        private int orderId;
        private RectTransform Tray => (RectTransform)transform;
        private Vector2 Point(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, e.position, e.pressEventCamera, out var point);
            return point;
        }
        public void OnPointerDown(PointerEventData e)
        {
            if (!View.CanServeTray || !View.Capture(e.pointerId, true)) return;
            IsDragging = true; start = Point(e); home = Tray.anchoredPosition; orderId = View.Session.Order.Id;
        }
        public void OnDrag(PointerEventData e)
        {
            if (!IsDragging || !View.OwnsPointer(e.pointerId)) return;
            if (!View.CanServeTray || View.Session.Order.Id != orderId) { Cancel(); return; }
            var delta = Point(e) - start;
            Tray.anchoredPosition = home + new Vector2(Mathf.Clamp(delta.x, -40, 40), Mathf.Clamp(delta.y, 0, 120));
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (!IsDragging || !View.OwnsPointer(e.pointerId)) return;
            var delta = Point(e) - start;
            if (View.Session.Order.Id == orderId && View.CanServeTray && delta.y >= ForwardDistance && delta.y > Mathf.Abs(delta.x))
                View.Command(ActionKind.ServeOrder);
            Cancel();
        }
        public void OnBeginDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }
        private void Cancel() { Tray.anchoredPosition = home; IsDragging = false; View.ReleasePointer(); }
        private void OnDisable() { if (IsDragging) Cancel(); }
        private void OnApplicationFocus(bool focused) { if (!focused && IsDragging) Cancel(); }
        private void OnApplicationPause(bool paused) { if (paused && IsDragging) Cancel(); }
    }

    public sealed class ReadyPancakeDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IBeginDragHandler, IEndDragHandler
    {
        public GameView View;
        public PancakeState Pancake;
        private PancakeState target;
        private bool held;
        public void OnPointerDown(PointerEventData e)
        {
            if (Pancake == null || View.Session.FindTrayPancake(Pancake.Id) != Pancake || !View.Capture(e.pointerId, true, true)) return;
            held = true; target = Pancake; View.SelectReadyPancake(target.Id);
        }
        public void OnDrag(PointerEventData e)
        {
            if (!held || !View.OwnsPointer(e.pointerId)) return;
            if (View.Session.FindTrayPancake(target.Id) != target) { Cancel(); return; }
            View.MoveReadyGhost(target, e.position);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (!held || !View.OwnsPointer(e.pointerId)) return;
            if (View.OverTrash(e.position)) View.Command(ActionKind.Discard, target.Id);
            Cancel();
        }
        public void OnBeginDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }
        private void Cancel() { held = false; target = null; View.HideReadyGhost(); View.ReleasePointer(); }
        private void OnDisable() { if (held) Cancel(); }
    }

    public sealed class IngredientDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IBeginDragHandler, IEndDragHandler
    {
        public GameView View;
        public int Ingredient;
        private bool held;
        public void OnPointerDown(PointerEventData e)
        {
            if (!View.Capture(e.pointerId)) return;
            held = true; View.SelectIngredient(Ingredient);
        }
        public void OnDrag(PointerEventData e) { if (held && Ingredient >= 3) View.MovePortionGhost(Ingredient, e.position); }
        public void OnPointerUp(PointerEventData e)
        {
            if (!held || !View.OwnsPointer(e.pointerId)) return;
            if (Ingredient >= 3 && View.OverPlate(e.position) && !View.Session.StartProfile.HelperEnabled((Ingredient)Ingredient))
            { var p = View.PlateCoordinates(e.position); View.Command(ActionKind.Portion, Ingredient, Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y)); }
            held = false; View.ReleasePointer(); View.HidePortionGhost();
        }
        public void OnBeginDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }
        private void OnDisable() { if (held) { held = false; View?.ReleasePointer(); View?.HidePortionGhost(); } }
    }
}
