using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace RBS.SleepKit2.Script
{
    /// <summary>
    /// シーン内に <c>YamaPlayer</c> コンポーネントが存在するかを検出するクラスです。
    /// 検出結果に基づいて、アクティブ状態更新対象およびタグ更新対象のオブジェクトを
    /// 個別に更新します。さらに、オプションで設定された yamaPlayerController の
    /// public フィールド "YamaPlayer" に、最初に検出された YamaPlayer インスタンスを代入します。
    /// </summary>
    public class YamaPlayerDetector : ComponentDetectorBase
    {
        /// <summary>
        /// YamaPlayerController への参照です。
        /// インスペクタから設定し、このコンポーネントの public フィールド "YamaPlayer" に
        /// 検出された YamaPlayer インスタンスを代入します。
        /// </summary>
        [SerializeField]
        private Component yamaPlayerController = null;

#if UNITY_EDITOR
        // Refresh内でシーン保存を行わないため不要
        private static bool isSavingScene = false;
#endif

        /// <inheritdoc/>
        public override void Refresh()
        {
            // 更新対象が登録されていなければ何もしない
            if (
                (activeStateTargets == null || activeStateTargets.Count == 0)
                && (editorOnlyTagTargets == null || editorOnlyTagTargets.Count == 0)
            )
            {
                return;
            }

            // --- YamaPlayer コンポーネントの検出 ---
            // 全アセンブリから "YamaPlayer" 型を検索
            Type yamaPlayerType = AppDomain
                .CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Yamadev.YamaStream.Script.YamaPlayer"))
                .FirstOrDefault(type => type != null);

            bool detectionResult = false;
            if (yamaPlayerType != null)
            {
                // Resources.FindObjectsOfTypeAll を使用して、非アクティブなオブジェクトも含めて検出する
                // ただし、この検出器が属するシーンのオブジェクトに限定してフィルタする
                UnityEngine.Object[] allObjects = Resources.FindObjectsOfTypeAll(yamaPlayerType);
                var foundObjects = allObjects
                    .Where(o =>
                    {
                        Component comp = o as Component;
                        return comp != null && comp.gameObject.scene == this.gameObject.scene;
                    })
                    .ToArray();

                detectionResult = (foundObjects != null && foundObjects.Length > 0);
            }

            // 検出結果に基づいて、各対象を個別に更新
            SetActiveStateForTargets(detectionResult);
            SetTagForTargets(detectionResult);
            previousDetectionState = detectionResult;

            bool updated = false;

            // --- yamaPlayerController の更新（検出結果が true の場合） ---
            if (detectionResult && yamaPlayerController != null && yamaPlayerType != null)
            {
                Type controllerType = yamaPlayerController.GetType();
                FieldInfo fieldInfo = controllerType.GetField(
                    "YamaPlayer",
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (fieldInfo != null && fieldInfo.FieldType.IsAssignableFrom(yamaPlayerType))
                {
                    // シーン内に属する YamaPlayer インスタンスをフィルタして最初のものを取得
                    UnityEngine.Object[] allObjects = Resources.FindObjectsOfTypeAll(
                        yamaPlayerType
                    );
                    var validObjects = allObjects
                        .Where(o =>
                        {
                            Component comp = o as Component;
                            return comp != null && comp.gameObject.scene == this.gameObject.scene;
                        })
                        .ToArray();

                    if (validObjects != null && validObjects.Length > 0)
                    {
                        // 最も近い YamaPlayer インスタンスを見つける
                        Component closestYamaPlayer = null;
                        float minDistance = float.MaxValue;
                        foreach (var yamaObj in validObjects)
                        {
                            Component yamaComp = yamaObj as Component;
                            if (yamaComp != null)
                            {
                                float distance = Vector3.Distance(
                                    this.transform.position,
                                    yamaComp.transform.position
                                );
                                if (distance < minDistance)
                                {
                                    minDistance = distance;
                                    closestYamaPlayer = yamaComp;
                                }
                            }
                        }

                        // 現在のフィールド値と異なる場合のみ更新
                        object currentValue = fieldInfo.GetValue(yamaPlayerController);
                        if (closestYamaPlayer != null && !Equals(currentValue, closestYamaPlayer))
                        {
                            fieldInfo.SetValue(yamaPlayerController, closestYamaPlayer);
                            updated = true;
                            Debug.Log(
                                $"[YamaPlayerDetector] yamaPlayerController の YamaPlayer フィールドに最も近い YamaPlayer インスタンス ({closestYamaPlayer.name}) を設定しました。"
                            );
                        }
                    }
                }
            }
#if UNITY_EDITOR

            var prev = Selection.activeGameObject;
            var inspectorType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
            var window = EditorWindow.GetWindow(inspectorType);
            Selection.activeGameObject = yamaPlayerController.gameObject;
            window?.Repaint();
            EditorApplication.delayCall += () =>
            {
                Selection.activeGameObject = prev;
            };

            // 更新があった場合は、対象オブジェクトとシーンを Dirty にし、シーンを保存する
            if (updated)
            {
                EditorUtility.SetDirty(yamaPlayerController);
                EditorSceneManager.MarkSceneDirty(yamaPlayerController.gameObject.scene);

                // Refreshメソッド内でのシーン保存は不要。
                // SceneSaveDetectorUpdaterがシーン保存直前にRefreshを呼び出すため、
                // Dirtyフラグを立てるだけでUnityの保存プロセスで保存される。
                //if (!isSavingScene)
                //{
                //    isSavingScene = true;
                //    // シーン保存ロジックは削除
                //    isSavingScene = false;
                //}
            }
#endif
        }
    }
}
