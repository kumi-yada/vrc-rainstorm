#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Editor;

namespace RBS.SleepKit2.Script.Editor
{
    /// <summary>
    /// シーン保存時に自動で、シーン内の ShaderSwitcherMarker コンポーネントの処理を呼び出すクラスです。
    /// 現在のビルドターゲット（PC/Android）に応じてシェーダーを切り替えます。
    /// </summary>
    [InitializeOnLoad]
    public class ShaderSwitcherUpdater : IActiveBuildTargetChanged
    {
        public int callbackOrder => 1;

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget)
        {
            // シーン内の全ての ShaderSwitcherMarker を取得
            var markers = GameObject.FindObjectsOfType<ShaderSwitcherMarker>(true);
            foreach (var marker in markers)
            {
                // ビルドターゲットに応じてシェーダーを切り替え
                marker.SwitchShadersBasedOnBuildTarget(newTarget);
            }
        }
    }
}
#endif
