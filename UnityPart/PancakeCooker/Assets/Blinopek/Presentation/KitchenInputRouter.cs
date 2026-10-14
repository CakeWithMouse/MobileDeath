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
        private bool painting, held;
        private Vector2 start;
        private PancakeState target;
        public void OnPointerDown(PointerEventData e)
        {
            if (!View.Capture(e.pointerId)) return;
            held = true; start = Coordinates(e.position); target = View.Session.Pancake;
            var stage = target == null ? PancakeStage.Batter : target.Stage;
            painting = Pan ? stage == PancakeStage.Batter : stage == PancakeStage.Plate && View.SelectedIngredient < 3;
            if (painting) View.Command(ActionKind.BeginStroke, Pan ? -1 : View.SelectedIngredient, (int)start.x, (int)start.y);
        }
        public void OnDrag(PointerEventData e)
        {
            if (!held || !View.OwnsPointer(e.pointerId)) return;
            if (target != null && View.Session.Pancake != target) { Cancel(); return; }
            var p = Coordinates(e.position);
            if (painting) View.Command(ActionKind.Stroke, (int)p.x, (int)p.y);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (!held || !View.OwnsPointer(e.pointerId)) return;
            var p = Coordinates(e.position);
            if (painting) View.Command(ActionKind.EndStroke);
            else if (target == View.Session.Pancake && target != null)
            {
                if (View.OverTrash(e.position)) View.Command(ActionKind.Discard);
                else if (Pan && View.OverPlate(e.position)) View.Command(ActionKind.MoveToPlate);
                else if (Pan && target.Stage == PancakeStage.SideA && (p - start).sqrMagnitude < 2500) View.Command(ActionKind.Flip);
                else if (!Pan && target.Stage == PancakeStage.Folding) View.Command(ActionKind.FoldGesture, (int)(p.x - start.x), (int)(p.y - start.y));
            }
            Cancel();
        }
        public void OnBeginDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }
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
