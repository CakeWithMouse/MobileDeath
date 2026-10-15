using System;
using Blinopek.Domain;
using Blinopek.Presentation;
using UnityEditor;
using UnityEngine;

namespace Blinopek.Editor
{
    public sealed class GuestArrivalBalanceWindow : EditorWindow
    {
        private GameContent content;
        private BalanceRules draft;
        private int previewPoints = 10;

        [MenuItem("Blinopek/Balance guest arrivals")]
        private static void Open() => GetWindow<GuestArrivalBalanceWindow>("Приход гостей");

        private void OnEnable()
        {
            minSize = new Vector2(480, 450);
            content = AssetDatabase.LoadAssetAtPath<GameContent>("Assets/Blinopek/Content/Gameplay.asset");
            Reload();
        }
        private void Reload() { draft = content != null ? content.balance.Copy() : null; }
        private static float Seconds(int ticks) => ticks * (DaySession.TickMilliseconds / 1000f);
        private static int TimeField(string label, int ticks)
        {
            float seconds = EditorGUILayout.FloatField(label, Seconds(ticks));
            return Mathf.RoundToInt(seconds * (1000f / DaySession.TickMilliseconds));
        }
        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            content = (GameContent)EditorGUILayout.ObjectField("Настройки игры", content, typeof(GameContent), false);
            if (EditorGUI.EndChangeCheck()) Reload();
            if (content == null || draft == null) return;
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 230;
            EditorGUILayout.HelpBox("Интервал считается от выдачи заказа или отказа гостя ждать. Украшения сокращают его, но не ускоряют реакцию и уход. Новые настройки действуют со следующей смены.", MessageType.Info);
            draft.guestArrivalBaseTicks = TimeField("Базовый интервал, с", draft.guestArrivalBaseTicks);
            draft.guestArrivalMinTicks = TimeField("Минимальный интервал, с", draft.guestArrivalMinTicks);
            draft.guestArrivalReductionPerDecorationPointTicks = TimeField("Сокращение за балл, с", draft.guestArrivalReductionPerDecorationPointTicks);
            draft.guestIntervalTicks = TimeField("Реакция и уход, с", draft.guestIntervalTicks);
            draft.darkTablesDecorationPoints = EditorGUILayout.IntField("Баллы тёмных столов", draft.darkTablesDecorationPoints);
            string error = null;
            try { draft.Validate(); }
            catch (ArgumentException) { error = "Значения должны быть неотрицательными. Минимальный интервал — не меньше времени ухода и не больше базового интервала."; }
            if (draft.guestArrivalBaseTicks == 0)
                EditorGUILayout.HelpBox("Базовый интервал 0 включает совместимость со старыми сохранениями: украшения не влияют на время. Укажите положительное время, чтобы включить баланс декора.", MessageType.Warning);
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
            using (new EditorGUI.DisabledScope(error != null)) {
                if (GUILayout.Button("Применить к следующим сменам")) {
                    // Only update these fields; other balance edits remain intact.
                    var serialized = new SerializedObject(content);
                    serialized.FindProperty("balance.guestArrivalBaseTicks").intValue = draft.guestArrivalBaseTicks;
                    serialized.FindProperty("balance.guestArrivalMinTicks").intValue = draft.guestArrivalMinTicks;
                    serialized.FindProperty("balance.guestArrivalReductionPerDecorationPointTicks").intValue = draft.guestArrivalReductionPerDecorationPointTicks;
                    serialized.FindProperty("balance.guestIntervalTicks").intValue = draft.guestIntervalTicks;
                    serialized.FindProperty("balance.darkTablesDecorationPoints").intValue = draft.darkTablesDecorationPoints;
                    serialized.ApplyModifiedProperties(); AssetDatabase.SaveAssetIfDirty(content);
                }
            }
            if (GUILayout.Button("Перечитать сохранённые настройки")) Reload();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Предпросмотр", EditorStyles.boldLabel);
            previewPoints = Math.Max(0, EditorGUILayout.IntField("Баллы украшенности", previewPoints));
            if (error == null) {
                int interval = draft.GetGuestArrivalIntervalTicks(previewPoints);
                EditorGUILayout.LabelField("Приход следующего гостя", Seconds(interval).ToString("0.##") + " с");
                EditorGUILayout.LabelField("Пустая стойка после ухода", Seconds(Math.Max(0, interval - draft.guestIntervalTicks)).ToString("0.##") + " с");
                EditorGUILayout.LabelField("Без улучшения столов", Seconds(draft.GetGuestArrivalIntervalTicks(0)).ToString("0.##") + " с");
                EditorGUILayout.LabelField("С тёмными столами", Seconds(draft.GetGuestArrivalIntervalTicks(draft.darkTablesDecorationPoints)).ToString("0.##") + " с");
            }
            EditorGUIUtility.labelWidth = previousLabelWidth;
        }
    }
}
