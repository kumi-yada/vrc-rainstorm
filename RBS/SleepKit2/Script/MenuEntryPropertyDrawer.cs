#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace RBS.SleepKit2.Script
{
    [CustomPropertyDrawer(typeof(MenuGenerator.MenuEntry))]
    public class MenuEntryPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // 各行の高さと行間
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            // 各フィールドの表示位置を設定
            Rect targetRect = new Rect(position.x, position.y, position.width, lineHeight);
            Rect actionRect = new Rect(
                position.x,
                position.y + lineHeight + spacing,
                position.width,
                lineHeight
            );
            Rect labelRect = new Rect(
                position.x,
                position.y + 2 * (lineHeight + spacing),
                position.width,
                lineHeight
            );

            // 各フィールドの SerializedProperty を取得
            SerializedProperty targetProp = property.FindPropertyRelative("target");
            SerializedProperty actionProp = property.FindPropertyRelative("action");
            SerializedProperty labelProp = property.FindPropertyRelative("label");

            // カスタムの表示用ラベルを作成（ここで見た目の文字列を変更）
            GUIContent targetLabel = new GUIContent("ターゲットオブジェクト");
            GUIContent actionLabel = new GUIContent("アクション");
            GUIContent displayLabel = new GUIContent("ボタンのタイトル");

            // ターゲットフィールドを描画
            EditorGUI.PropertyField(targetRect, targetProp, targetLabel);

            // ActionType のフィールドを、Popup を使って描画
            // 現在の選択状態（enumValueIndex）を取得
            int currentIndex = actionProp.enumValueIndex;
            // カスタム表示する選択肢
            string[] actionOptions = new string[]
            {
                "手元にテレポートさせる",
                "オン・オフ切り替えする",
            };
            int newIndex = EditorGUI.Popup(
                actionRect,
                actionLabel.text,
                currentIndex,
                actionOptions
            );
            actionProp.enumValueIndex = newIndex;

            // 表示ラベルフィールドを描画
            EditorGUI.PropertyField(labelRect, labelProp, displayLabel);

            EditorGUI.EndProperty();
        }

        // 各フィールドの高さを合計して返す
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            // 3行分の高さ + 2行分のスペース
            return 3 * lineHeight + 2 * spacing;
        }
    }
}
#endif
