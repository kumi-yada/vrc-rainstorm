using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RBS.SleepKit2.Script
{
    /// <summary>
    /// マーカークラス：シーン内のマテリアルのシェーダーをプラットフォームごとに切り替えるための設定を保持します。
    /// このコンポーネントをシーン内に配置して、マテリアルとそれに対応するPC用/Quest用シェーダーを設定してください。
    /// シーン保存時に、現在のビルドターゲット（PC/Android）に応じて自動的にシェーダーが切り替わります。
    /// </summary>
    [ExecuteAlways]
    public class ShaderSwitcherMarker : MonoBehaviour, IEditorOnly
    {
        [System.Serializable]
        public class MaterialShaderPair
        {
            public Material material;
            public Shader pcShader;
            public Shader questShader;

            [Tooltip("現在のシェーダー状態を表示します（読み取り専用）")]
            public string currentShaderName;
        }

        [SerializeField]
        [Tooltip("シェーダー切り替え対象のマテリアルとシェーダーのペアリスト")]
        private List<MaterialShaderPair> materialShaderPairs = new List<MaterialShaderPair>();

        [SerializeField]
        [Tooltip("シェーダー切り替え時にログを出力するかどうか")]
        private bool enableLogging = true;

#if UNITY_EDITOR
        /// <summary>
        /// 現在のビルドターゲットに基づいてシェーダーを切り替えます。
        /// このメソッドはシーン保存時に自動的に呼び出されます。
        /// </summary>
        /// <param name="buildTarget">ビルドターゲット</param>
        public void SwitchShadersBasedOnBuildTarget(BuildTarget buildTarget)
        {
            bool isQuest = (buildTarget == BuildTarget.Android);
            bool anyChanged = false;

            foreach (var pair in materialShaderPairs)
            {
                if (pair.material != null)
                {
                    Shader targetShader = isQuest ? pair.questShader : pair.pcShader;

                    // 現在のシェーダーと異なる場合のみ切り替え
                    if (targetShader != null && pair.material.shader != targetShader)
                    {
                        // 変更前のシェーダー名を保存
                        string oldShaderName =
                            pair.material.shader != null ? pair.material.shader.name : "None";

                        // シェーダーを切り替え
                        pair.material.shader = targetShader;

                        // 現在のシェーダー名を更新
                        pair.currentShaderName = targetShader.name;

                        // ログ出力
                        if (enableLogging)
                        {
                            Debug.Log(
                                $"[ShaderSwitcher] マテリアル '{pair.material.name}' のシェーダーを {(isQuest ? "Quest" : "PC")} 用に切り替えました: {oldShaderName} -> {targetShader.name}"
                            );
                        }

                        // マテリアルを変更したことをUnityに通知
                        EditorUtility.SetDirty(pair.material);
                        anyChanged = true;
                    }
                    else if (targetShader != null)
                    {
                        // 既に正しいシェーダーが設定されている場合は現在のシェーダー名を更新するだけ
                        pair.currentShaderName = pair.material.shader.name;
                    }
                }
            }

            // 変更があった場合はマーカー自体も変更されたとマーク
            if (anyChanged)
            {
                EditorUtility.SetDirty(this);
            }
        }

        /// <summary>
        /// インスペクタでの値変更時に現在のシェーダー名を更新します。
        /// </summary>
        private void OnValidate()
        {
            foreach (var pair in materialShaderPairs)
            {
                if (pair.material != null && pair.material.shader != null)
                {
                    pair.currentShaderName = pair.material.shader.name;
                }
                else
                {
                    pair.currentShaderName = "None";
                }
            }
        }
#endif
    }

#if UNITY_EDITOR
    // ReadOnly属性の実装
    public class ReadOnlyAttribute : PropertyAttribute { }

    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public class ReadOnlyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // GUIを無効化して表示のみ行う
            GUI.enabled = false;
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }
    }
#endif
}
