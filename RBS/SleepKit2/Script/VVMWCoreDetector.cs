using System;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace RBS.SleepKit2.Script
{
    /// <summary>
    /// シーン内に <c>JLChnToZ.VRC.VVMW.Core</c> コンポーネントが存在するかを検出し、
    /// UIHandler の core フィールド（非公開の場合も対応）にシーン内の最初の Core インスタンス、
    /// handler フィールドにシーン内の最初の FrontendHandler インスタンスを設定します。
    /// また、検出結果に基づいてアクティブ状態更新対象およびタグ更新対象のオブジェクトの更新も行います。
    /// 更新があった場合は、対象オブジェクトおよびシーンを Dirty にしてシーンのセーブも行います。
    /// </summary>
    public class VVMWCoreDetector : ComponentDetectorBase
    {
        [Tooltip("対象の UIHandler コンポーネントをインスペクタで指定してください")]
        public Component uiHandler; // インスペクタから UIHandler コンポーネントを指定

        // シーン保存中の再入を防止するための静的フラグ
        //#if UNITY_EDITOR
        // Refresh内でシーン保存を行わないため不要
        //                private static bool isSavingScene = false;
        //#endif

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

            // --- Core コンポーネントの検出 ---
            Type coreType = AppDomain
                .CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("JLChnToZ.VRC.VVMW.Core"))
                .FirstOrDefault(t => t != null);
            UnityEngine.Object[] coreObjects = new UnityEngine.Object[0];
            bool detectionResult = false;
            if (coreType != null)
            {
                UnityEngine.Object[] allCoreObjects = Resources.FindObjectsOfTypeAll(coreType);
                coreObjects = allCoreObjects
                    .Where(o =>
                    {
                        Component comp = o as Component;
                        return comp != null && comp.gameObject.scene == this.gameObject.scene;
                    })
                    .ToArray();
                detectionResult = (coreObjects != null && coreObjects.Length > 0);
            }
            else
            {
                Debug.LogWarning(
                    "[VVMWCoreDetector] Core 型 (JLChnToZ.VRC.VVMW.Core) が見つかりませんでした。"
                );
            }

            // --- 既存の更新処理 ---
            SetActiveStateForTargets(detectionResult);
            SetTagForTargets(detectionResult);
            previousDetectionState = detectionResult;

            // --- UIHandler へのフィールド設定処理 ---
            if (uiHandler == null)
            {
                Debug.LogWarning(
                    "[VVMWCoreDetector] UIHandler がインスペクタで指定されていません。"
                );
                return;
            }

            Type uiHandlerType = uiHandler.GetType();

            // 更新が発生したかを判定するためのフラグ
            bool updated = false;

            // 1. UIHandler の core フィールドに、シーン内で見つかった最も近い Core インスタンスを代入
            if (coreObjects.Length > 0 && coreType != null)
            {
                // 最も近い Core インスタンスを見つける
                Component closestCore = null;
                float minDistance = float.MaxValue;
                foreach (var coreObj in coreObjects)
                {
                    Component coreComp = coreObj as Component;
                    if (coreComp != null)
                    {
                        float distance = Vector3.Distance(
                            this.transform.position,
                            coreComp.transform.position
                        );
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            closestCore = coreComp;
                        }
                    }
                }

                // 非公開フィールドも検索するため BindingFlags.Instance | BindingFlags.NonPublic を指定
                var coreField = uiHandlerType.GetField(
                    "core",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                );
                if (coreField != null)
                {
                    // 現在の値と異なる場合のみ更新
                    var currentValue = coreField.GetValue(uiHandler);
                    if (closestCore != null && !Equals(currentValue, closestCore)) // closestCore を使用
                    {
                        coreField.SetValue(uiHandler, closestCore); // closestCore を使用
                        updated = true;
                        Debug.Log(
                            $"[VVMWCoreDetector] UIHandler の core フィールドに最も近い Core インスタンス ({closestCore.name}) を設定しました。"
                        );
                    }
                }
                else
                {
                    Debug.LogWarning(
                        "[VVMWCoreDetector] UIHandler の core フィールドが見つかりませんでした。"
                    );
                }
            }
            else
            {
                Debug.LogWarning(
                    "[VVMWCoreDetector] シーン内に Core コンポーネントが見つかりませんでした。"
                );
            }

            // 2. UIHandler の handler フィールドに、シーン内で見つかった最初の FrontendHandler インスタンスを代入
            Type frontendType = AppDomain
                .CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("JLChnToZ.VRC.VVMW.FrontendHandler"))
                .FirstOrDefault(t => t != null);
            if (frontendType == null)
            {
                Debug.LogWarning("[VVMWCoreDetector] FrontendHandler 型が見つかりませんでした。");
                return;
            }

            UnityEngine.Object[] allFrontend = Resources.FindObjectsOfTypeAll(frontendType);
            var frontendObjects = allFrontend
                .Where(o =>
                {
                    Component comp = o as Component;
                    return comp != null && comp.gameObject.scene == this.gameObject.scene;
                })
                .ToArray();

            if (frontendObjects.Length > 0 && frontendType != null) // frontendType の null チェックを追加
            {
                // 最も近い FrontendHandler インスタンスを見つける
                Component closestFrontend = null;
                float minDistance = float.MaxValue;
                foreach (var frontendObj in frontendObjects)
                {
                    Component frontendComp = frontendObj as Component;
                    if (frontendComp != null)
                    {
                        float distance = Vector3.Distance(
                            this.transform.position,
                            frontendComp.transform.position
                        );
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            closestFrontend = frontendComp;
                        }
                    }
                }
                // こちらも非公開フィールドも対象にするために BindingFlags.NonPublic を指定
                var handlerField = uiHandlerType.GetField(
                    "handler",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                );
                if (handlerField != null)
                {
                    var currentValue = handlerField.GetValue(uiHandler);
                    if (closestFrontend != null && !Equals(currentValue, closestFrontend)) // closestFrontend を使用
                    {
                        handlerField.SetValue(uiHandler, closestFrontend); // closestFrontend を使用
                        updated = true;
                        Debug.Log(
                            $"[VVMWCoreDetector] UIHandler の handler フィールドに最も近い FrontendHandler インスタンス ({closestFrontend.name}) を設定しました。"
                        );
                    }
                }
                else
                {
                    Debug.LogWarning(
                        "[VVMWCoreDetector] UIHandler の handler フィールドが見つかりませんでした。"
                    );
                }
            }
            else
            {
                Debug.LogWarning(
                    "[VVMWCoreDetector] シーン内に FrontendHandler コンポーネントが見つかりませんでした。"
                );
            }

#if UNITY_EDITOR
            if (updated)
            {
                // uiHandler とシーンを Dirty にする
                EditorUtility.SetDirty(uiHandler);
                EditorSceneManager.MarkSceneDirty(uiHandler.gameObject.scene);

                // Refreshメソッド内でのシーン保存は不要。
                // SceneSaveDetectorUpdaterがシーン保存直前にRefreshを呼び出すため、
                // Dirtyフラグを立てるだけでUnityの保存プロセスで保存される。
                // シーン保存ロジックは削除
            }
#endif
        }
    }
}
