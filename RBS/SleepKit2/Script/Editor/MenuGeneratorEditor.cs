#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace RBS.SleepKit2.Script
{
    [CustomEditor(typeof(MenuGenerator))]
    public class MenuGeneratorEditor : UnityEditor.Editor
    {
        // Developer Settings の表示／非表示用フラグ（登録項目のみの表示）
        private bool showDeveloperSettings = false;

        // scriptReferences 配列に対応する UnityEditor.Editor インスタンスのキャッシュ
        private UnityEditor.Editor[] scriptEditors = null;

        public override void OnInspectorGUI()
        {
            // シリアライズオブジェクトの更新
            serializedObject.Update();

            // --- Header Image の表示 ---
            // headerImageGUID プロパティを取得して表示
            SerializedProperty headerImageGUIDProp = serializedObject.FindProperty(
                "headerImageGUID"
            );

            // headerImageGUID に値がある場合、GUID からアセットパスを取得し、Texture2D として読み込む
            if (!string.IsNullOrEmpty(headerImageGUIDProp.stringValue))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(headerImageGUIDProp.stringValue);
                Texture2D headerImage = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (headerImage != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(
                        headerImage,
                        GUILayout.Width(204.8f * 2f),
                        GUILayout.Height(51.2f * 2f)
                    );
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space();

            GUIStyle largeStyle = new GUIStyle(EditorStyles.boldLabel);
            largeStyle.fontSize = 16;

            // UIの色の表示セクション
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("UIの色", largeStyle);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetColor"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Menu Entries の表示部分を、UI上だけ「メニュー内容」という名前に変更して描画
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("オブジェクトメニュー", largeStyle);
            GUIContent customMenuEntriesLabel = new GUIContent("メニュー内容");
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("menuEntries"),
                customMenuEntriesLabel,
                true
            );
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // --- スクリプトの中身の描画（常時表示） ---
            // 登録済みの scriptReferences 配列の各要素に対して、
            // 対象スクリプトの Inspector 表示内容を描画します。
            SerializedProperty scriptRefsForDrawing = serializedObject.FindProperty(
                "scriptReferences"
            );
            int arraySize = scriptRefsForDrawing.arraySize;
            // キャッシュの再構築（配列サイズが変わった場合など）
            if (scriptEditors == null || scriptEditors.Length != arraySize)
            {
                if (scriptEditors != null)
                {
                    for (int i = 0; i < scriptEditors.Length; i++)
                    {
                        if (scriptEditors[i] != null)
                        {
                            DestroyImmediate(scriptEditors[i]);
                        }
                    }
                }
                scriptEditors = new UnityEditor.Editor[arraySize];
            }

            // 各スクリプトの Inspector 描画
            for (int i = 0; i < arraySize; i++)
            {
                SerializedProperty elementProp = scriptRefsForDrawing.GetArrayElementAtIndex(i);
                Object scriptObj = elementProp.objectReferenceValue;
                if (scriptObj != null)
                {
                    // キャッシュがない、または対象が変更された場合は再生成
                    if (scriptEditors[i] == null || scriptEditors[i].target != scriptObj)
                    {
                        if (scriptEditors[i] != null)
                        {
                            DestroyImmediate(scriptEditors[i]);
                        }
                        scriptEditors[i] = UnityEditor.Editor.CreateEditor(scriptObj);
                    }

                    EditorGUILayout.BeginVertical("box");

                    // MonoBehaviour としてキャストし、アタッチされているゲームオブジェクトの名前を取得
                    MonoBehaviour monoScript = scriptObj as MonoBehaviour;
                    string goName =
                        monoScript != null && monoScript.gameObject != null
                            ? monoScript.gameObject.name
                            : "Unknown";
                    EditorGUILayout.LabelField(goName, largeStyle);

                    // スクリプトの Inspector 描画
                    scriptEditors[i].OnInspectorGUI();
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.Space();
                }
            }

            // --- Developer Settings 部分 ---
            showDeveloperSettings = EditorGUILayout.Foldout(
                showDeveloperSettings,
                "【Developer Settings】",
                true
            );
            if (showDeveloperSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(headerImageGUIDProp);

                // scriptReferences 配列の登録（要素の追加／削除が可能）
                SerializedProperty scriptRefsProp = serializedObject.FindProperty(
                    "scriptReferences"
                );
                EditorGUILayout.PropertyField(scriptRefsProp, true);

                // その他 Developer Settings 内の設定項目
                EditorGUILayout.PropertyField(serializedObject.FindProperty("menuPrefab"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("menuParent"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("callDestination"));
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("disableWhenNoMenuEntries")
                );
                EditorGUI.indentLevel--;
            }

            // 変更の適用
            serializedObject.ApplyModifiedProperties();
        }

        // エディタが破棄される際にキャッシュした UnityEditor.Editor インスタンスを破棄
        private void OnDisable()
        {
            if (scriptEditors != null)
            {
                for (int i = 0; i < scriptEditors.Length; i++)
                {
                    if (scriptEditors[i] != null)
                    {
                        DestroyImmediate(scriptEditors[i]);
                    }
                }
            }
        }
    }
}
#endif
