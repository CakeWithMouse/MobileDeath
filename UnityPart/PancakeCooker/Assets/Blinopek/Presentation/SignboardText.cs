using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    // Align actual glyph bounds on the painted board, rather than the font's line box.
    // The font atlas stays dynamic so any player-entered name receives the same wear.
    public sealed class SignboardText : Text
    {
        private Material paint;
        public void SetPaint(Shader shader)
        {
            if (shader == null) return;
            paint = new Material(shader) { name = "Signboard paint", hideFlags = HideFlags.HideAndDontSave };
            material = paint;
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            base.OnPopulateMesh(mesh);
            if (mesh.currentVertCount == 0) return;
            var vertex = new UIVertex();
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < mesh.currentVertCount; i++) {
                mesh.PopulateUIVertex(ref vertex, i);
                min = Vector2.Min(min, vertex.position); max = Vector2.Max(max, vertex.position);
            }
            Vector2 size = max - min, center = (max + min) * .5f;
            float fit = Mathf.Min(68f / Mathf.Max(1, size.y), (rectTransform.rect.width - 40) / Mathf.Max(1, size.x));
            for (int i = 0; i < mesh.currentVertCount; i++) {
                mesh.PopulateUIVertex(ref vertex, i);
                Vector2 position = ((Vector2)vertex.position - center) * fit + rectTransform.rect.center;
                vertex.position = new Vector3(position.x, position.y, vertex.position.z);
                mesh.SetUIVertex(vertex, i);
            }
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (paint != null) {
                if (UnityEngine.Application.isPlaying) Destroy(paint); else DestroyImmediate(paint);
            }
        }
    }
}
