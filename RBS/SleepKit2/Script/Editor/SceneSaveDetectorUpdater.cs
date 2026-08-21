#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RBS.SleepKit2.Script
{
    [InitializeOnLoad]
    public static class SceneSaveDetectorUpdater
    {
        // 再入防止用フラグ
        private static bool isRefreshing = false;
        private static bool refreshScheduled = false; // 追加：すでに更新がスケジュールされているか

        static SceneSaveDetectorUpdater()
        {
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            // 既に更新処理がスケジュールされている場合は何もしない
            if (refreshScheduled)
                return;

            refreshScheduled = true;
            // 少し遅延させてから更新を実行
            EditorApplication.delayCall += () =>
            {
                if (isRefreshing)
                    return;

                try
                {
                    isRefreshing = true;
                    Debug.Log(
                        $"[SceneSaveDetectorUpdater] シーン '{scene.name}' の保存前に検出器を更新します。"
                    );

                    ComponentDetectorBase[] detectors =
                        GameObject.FindObjectsOfType<ComponentDetectorBase>(true);
                    if (detectors == null || detectors.Length == 0)
                    {
                        Debug.Log(
                            $"[SceneSaveDetectorUpdater] シーン '{scene.name}' に検出器コンポーネントが見つかりませんでした。"
                        );
                    }
                    else
                    {
                        foreach (ComponentDetectorBase detector in detectors)
                        {
                            if (detector != null)
                            {
                                detector.Refresh();
                            }
                        }
                        Debug.Log(
                            $"[SceneSaveDetectorUpdater] シーン '{scene.name}' の検出器 {detectors.Length} 件を更新しました。"
                        );
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(
                        $"[SceneSaveDetectorUpdater] 検出器更新中に例外が発生しました: {ex}"
                    );
                }
                finally
                {
                    isRefreshing = false;
                    refreshScheduled = false;
                }
            };
        }
    }
}
#endif
