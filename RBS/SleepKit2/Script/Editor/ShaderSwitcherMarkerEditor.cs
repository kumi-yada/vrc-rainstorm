#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RBS.SleepKit2.Script.Editor
{
    /// <summary>
    /// ShaderSwitcherMarkerのカスタムエディタ。
    /// インスペクタからPC用/Quest用シェーダーに手動で切り替えるボタンを提供します。
    /// </summary>
    [CustomEditor(typeof(ShaderSwitcherMarker))]
    public class ShaderSwitcherMarkerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // 元のインスペクタを描画
            DrawDefaultInspector();

            // ターゲットを取得
            ShaderSwitcherMarker marker = (ShaderSwitcherMarker)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("シェーダー切り替え", EditorStyles.boldLabel);

            // 現在のビルドターゲットを表示
            BuildTarget currentTarget = EditorUserBuildSettings.activeBuildTarget;
            EditorGUILayout.LabelField("現在のビルドターゲット:", currentTarget.ToString());

            EditorGUILayout.Space(5);

            // PC用シェーダーに切り替えるボタン
            if (GUILayout.Button("PC用シェーダーに切り替え"))
            {
                marker.SwitchShadersBasedOnBuildTarget(BuildTarget.StandaloneWindows64);
                EditorUtility.SetDirty(marker);
                SceneView.RepaintAll();
            }

            // Quest用シェーダーに切り替えるボタン
            if (GUILayout.Button("Quest用シェーダーに切り替え"))
            {
                marker.SwitchShadersBasedOnBuildTarget(BuildTarget.Android);
                EditorUtility.SetDirty(marker);
                SceneView.RepaintAll();
            }

            // 現在のビルドターゲットに合わせて切り替えるボタン
            if (GUILayout.Button("現在のビルドターゲットに合わせて切り替え"))
            {
                marker.SwitchShadersBasedOnBuildTarget(currentTarget);
                EditorUtility.SetDirty(marker);
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "シーン保存時に自動的に現在のビルドターゲットに応じたシェーダーに切り替わります。手動で切り替える場合は上のボタンを使用してください。",
                MessageType.Info
            );
        }
    }
}
#endif
