#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RBS.SleepKit2.Udon; // 対象のクラスの名前空間
using System.Collections.Generic; // Listを使用するため追加
using System.Linq; // Linqを使用するため追加

/// <summary>
/// SleepKitSettingsManagerのカスタムエディタ。
/// シーン保存時にインスタンスの存在チェックと自動生成（プレハブから）、
/// および関連するPersistenceManagerのprefix自動修正を行います。
/// </summary>
[InitializeOnLoad] // エディタ起動時とスクリプトコンパイル時に初期化
public class SleepKitSettingsManagerEditor
{
    // !!! ユーザー編集箇所: 自動生成に使用するプレハブのGUIDを入力してください !!!
    // 例: private const string PrefabGuid = "abcdef1234567890abcdef1234567890";
    private const string PrefabGuid = "00e64f10c7c6709418a1957a68636939"; // ← ここにSleepKitSettingsManagerPrefabのGUIDを入力

    private static bool isProcessing = false; // 再入防止フラグ
    private const string DefaultPrefix = "RBS_SLK2_";
    private const string InstancePrefixBase = "RBS_SLK2_Instance_";

    // 静的コンストラクタでイベントハンドラを登録
    static SleepKitSettingsManagerEditor()
    {
        EditorSceneManager.sceneSaving += OnSceneSaving;
        EditorApplication.quitting += OnEditorQuitting;
    }

    // エディタ終了時にイベントハンドラを解除
    private static void OnEditorQuitting()
    {
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        EditorApplication.quitting -= OnEditorQuitting;
    }

    // シーン保存時に呼び出されるメソッド
    private static void OnSceneSaving(Scene scene, string path)
    {
        if (isProcessing)
            return; // 処理中の場合は再入しない

        isProcessing = true;
        try
        {
            bool sceneChanged = false;
            bool prefixChanged = false;

            // --- SleepKitSettingsManager, JoinToastManager, ToastManager のチェック --- // ToastManagerSettings -> JoinToastManager
            SleepKitSettingsManager[] ssmManagers =
                Object.FindObjectsOfType<SleepKitSettingsManager>();
            JoinToastManager[] jtmManagers = Object.FindObjectsOfType<JoinToastManager>(); // ToastManagerSettings -> JoinToastManager
            ToastManager[] tmManagers = Object.FindObjectsOfType<ToastManager>();

            SleepKitSettingsManager targetSsmManager = null;
            JoinToastManager targetJtmManager = null; // jtm を取得しておく

            // SleepKitSettingsManager のチェックと自動生成
            if (ssmManagers.Length == 0)
            {
                // SleepKit2インスタンスが存在する場合のみ自動生成を試みる
                if (
                    Object.FindObjectsOfType<AlarmManager>().Length > 0
                    || Object.FindObjectsOfType<NightModeManager>().Length > 0
                )
                {
                    GameObject instantiatedPrefab = InstantiateSingletonPrefab();
                    if (instantiatedPrefab != null)
                    {
                        targetSsmManager =
                            instantiatedPrefab.GetComponent<SleepKitSettingsManager>();
                        if (targetSsmManager != null)
                        {
                            sceneChanged = true;
                            // プレハブ内の参照設定と、シーン内の他コンポーネントへの参照設定
                            SetupPrefabInstanceReferences(instantiatedPrefab, targetSsmManager);
                            // 配列を再取得しておく
                            ssmManagers = new SleepKitSettingsManager[] { targetSsmManager };
                            jtmManagers = Object.FindObjectsOfType<JoinToastManager>(); // 再取得
                            tmManagers = Object.FindObjectsOfType<ToastManager>();
                            if (jtmManagers.Length > 0)
                                targetJtmManager = jtmManagers[0]; // targetJtmManagerを設定
                        }
                        else
                        {
                            Debug.LogError(
                                $"[SleepKitSettingsManagerEditor] 生成されたプレハブインスタンス '{instantiatedPrefab.name}' に SleepKitSettingsManager コンポーネントが見つかりません。"
                            );
                            Object.DestroyImmediate(instantiatedPrefab); // 失敗したら破棄
                        }
                    }
                    // InstantiateSingletonPrefab内でエラーログは出力される
                }
            }
            else if (ssmManagers.Length > 1)
            {
                Debug.LogError(
                    $"[SleepKitSettingsManagerEditor] シーン内に複数の SleepKitSettingsManager が存在します ({ssmManagers.Length}個)。1つだけにしてください。"
                );
            }
            else
            {
                targetSsmManager = ssmManagers[0]; // 唯一のManagerを取得
                if (jtmManagers.Length > 0)
                    targetJtmManager = jtmManagers[0]; // 既存のjtmを取得
            }

            // JoinToastManager と ToastManager の数チェック (エラー表示のみ)
            if (targetSsmManager != null) // Managerが存在する場合のみチェック
            {
                if (jtmManagers.Length == 0)
                {
                    Debug.LogError(
                        "[SleepKitSettingsManagerEditor] シーン内に JoinToastManager が見つかりません。シングルトンプレハブに含まれているか確認してください。"
                    );
                }
                else if (jtmManagers.Length > 1)
                {
                    Debug.LogError(
                        $"[SleepKitSettingsManagerEditor] シーン内に複数の JoinToastManager が存在します ({jtmManagers.Length}個)。1つだけにしてください。"
                    );
                }

                if (tmManagers.Length == 0)
                {
                    Debug.LogError(
                        "[SleepKitSettingsManagerEditor] シーン内に ToastManager が見つかりません。シングルトンプレハブに含まれているか確認してください。"
                    );
                }
                else if (tmManagers.Length > 1)
                {
                    Debug.LogError(
                        $"[SleepKitSettingsManagerEditor] シーン内に複数の ToastManager が存在します ({tmManagers.Length}個)。1つだけにしてください。"
                    );
                }
            }

            // --- SleepKitUIManager の参照設定 (targetJtmManager が確定してから行う) ---
            if (targetJtmManager != null)
            {
                SetupUIManagerReferences(targetJtmManager); // 新しいメソッドで処理
            }

            // --- PersistenceManagerのprefixチェックと修正 ---
            if (targetSsmManager != null)
            {
                prefixChanged = CheckAndFixPersistencePrefixes();
            }

            // シーンに変更があった場合のみMarkDirtyを呼び出す
            if (sceneChanged || prefixChanged)
            {
                Debug.Log(
                    "[SleepKitSettingsManagerEditor] シーンに変更があったため、保存を促します。"
                );
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }
        finally
        {
            isProcessing = false; // 処理完了時にフラグを解除
        }
    }

    // シングルトンプレハブをインスタンス化するヘルパーメソッド
    private static GameObject InstantiateSingletonPrefab()
    {
        if (string.IsNullOrEmpty(PrefabGuid))
        {
            Debug.LogError(
                "[SleepKitSettingsManagerEditor] 自動生成用のプレハブGUIDが設定されていません。スクリプトを編集してください。"
            );
            return null;
        }

        string prefabPath = AssetDatabase.GUIDToAssetPath(PrefabGuid);
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogError(
                $"[SleepKitSettingsManagerEditor] GUID '{PrefabGuid}' に対応するアセットが見つかりません。"
            );
            return null;
        }

        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabAsset == null)
        {
            Debug.LogError(
                $"[SleepKitSettingsManagerEditor] パス '{prefabPath}' からプレハブをロードできませんでした。"
            );
            return null;
        }

        Debug.Log(
            $"[SleepKitSettingsManagerEditor] シーン内に SleepKitSettingsManager が見つかりません。プレハブ '{prefabAsset.name}' から自動生成します。"
        );
        GameObject instantiatedPrefab = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
        if (instantiatedPrefab == null)
        {
            Debug.LogError(
                $"[SleepKitSettingsManagerEditor] プレハブ '{prefabAsset.name}' のインスタンス化に失敗しました。"
            );
            return null;
        }
        return instantiatedPrefab;
    }

    // プレハブインスタンス内の参照設定と、シーン内の他コンポーネントへの参照設定を行うヘルパーメソッド
    private static void SetupPrefabInstanceReferences(
        GameObject instance,
        SleepKitSettingsManager ssm
    )
    {
        if (instance == null || ssm == null)
            return;

        // --- プレハブ内部の参照設定 ---
        JoinToastManager jtm = instance.GetComponentInChildren<JoinToastManager>(true);
        ToastManager tm = instance.GetComponentInChildren<ToastManager>(true);

        if (jtm != null)
        {
            // SleepKitSettingsManager -> JoinToastManager
            if (ssm.joinToastManager == null)
            {
                ssm.joinToastManager = jtm;
                EditorUtility.SetDirty(ssm);
                Debug.Log(
                    $"[SleepKitSettingsManagerEditor] 生成された {ssm.gameObject.name} に JoinToastManager を設定しました。"
                );
            }

            // JoinToastManager -> ToastManager
            if (tm != null)
            {
                if (jtm.toastManager == null)
                {
                    jtm.toastManager = tm;
                    EditorUtility.SetDirty(jtm);
                    Debug.Log(
                        $"[SleepKitSettingsManagerEditor] 生成された {jtm.gameObject.name} に ToastManager を設定しました。"
                    );
                }
            }
            else
            {
                Debug.LogWarning(
                    $"[SleepKitSettingsManagerEditor] 生成されたプレハブインスタンス '{instance.name}' 内に ToastManager が見つかりません。JoinToastManager の参照が設定できません。"
                );
            }
        }
        else
        {
            Debug.LogWarning(
                $"[SleepKitSettingsManagerEditor] 生成されたプレハブインスタンス '{instance.name}' 内に JoinToastManager が見つかりません。SleepKitSettingsManager の参照が設定できません。"
            );
        }

        // --- シーン内の他コンポーネントへの参照設定 (SSM) ---
        AlarmManager[] alarmManagers = Object.FindObjectsOfType<AlarmManager>();
        foreach (var am in alarmManagers)
        {
            if (am.settingsManager == null)
            {
                am.settingsManager = ssm;
                EditorUtility.SetDirty(am);
                Debug.Log(
                    $"[SleepKitSettingsManagerEditor] {am.gameObject.name} の AlarmManager に SettingsManager を設定しました。"
                );
            }
        }

        NightModeManager[] nightModeManagers = Object.FindObjectsOfType<NightModeManager>();
        foreach (var nm in nightModeManagers)
        {
            if (nm.settingsManager == null)
            {
                nm.settingsManager = ssm;
                EditorUtility.SetDirty(nm);
                Debug.Log(
                    $"[SleepKitSettingsManagerEditor] {nm.gameObject.name} の NightModeManager に SettingsManager を設定しました。"
                );
            }
        }
    }

    // SleepKitUIManager への参照設定を行うヘルパーメソッド
    private static void SetupUIManagerReferences(JoinToastManager jtm)
    {
        if (jtm == null)
            return;

        SleepKitUIManager[] uiManagers = Object.FindObjectsOfType<SleepKitUIManager>();
        foreach (var uim in uiManagers)
        {
            // SleepKitUIManagerのjoinToastManagerフィールドはpublicである必要がある
            if (uim.joinToastManager == null)
            {
                uim.joinToastManager = jtm;
                EditorUtility.SetDirty(uim);
                Debug.Log(
                    $"[SleepKitSettingsManagerEditor] {uim.gameObject.name} の SleepKitUIManager に JoinToastManager を設定しました。"
                );
            }
        }
    }

    // PersistenceManagerのprefixをチェックし、必要なら修正するヘルパーメソッド
    private static bool CheckAndFixPersistencePrefixes()
    {
        bool changed = false;
        PersistenceManager[] persistenceManagers = Object.FindObjectsOfType<PersistenceManager>();
        List<PersistenceManager> sleepKitPersistenceManagers = new List<PersistenceManager>();
        HashSet<string> existingPrefixes = new HashSet<string>();
        int maxInstanceNumber = 0;

        // SleepKit2インスタンス内のPersistenceManagerを特定し、既存のPrefixを収集
        foreach (var pm in persistenceManagers)
        {
            // 同じGameObjectにAlarmManager, NightModeManager, SleepKitUIManager があればSleepKit2の一部とみなす
            if (
                pm.GetComponent<AlarmManager>() != null
                || pm.GetComponent<NightModeManager>() != null
                || pm.GetComponent<SleepKitUIManager>() != null // 判定対象に追加
            )
            {
                sleepKitPersistenceManagers.Add(pm);
                if (!string.IsNullOrEmpty(pm.prefix) && pm.prefix != DefaultPrefix)
                {
                    existingPrefixes.Add(pm.prefix);
                    // 連番形式のPrefixから最大番号を取得
                    if (pm.prefix.StartsWith(InstancePrefixBase))
                    {
                        string numberPart = pm
                            .prefix.Substring(InstancePrefixBase.Length)
                            .TrimEnd('_');
                        if (int.TryParse(numberPart, out int num))
                        {
                            if (num > maxInstanceNumber)
                            {
                                maxInstanceNumber = num;
                            }
                        }
                    }
                }
            }
        }

        // Prefixのチェックと修正
        foreach (var pm in sleepKitPersistenceManagers)
        {
            bool needsUpdate = false;
            string originalPrefix = pm.prefix;

            // デフォルト値のままか、他のインスタンスと重複しているかチェック
            if (
                string.IsNullOrEmpty(originalPrefix)
                || originalPrefix == DefaultPrefix
                || (
                    existingPrefixes.Contains(originalPrefix)
                    && sleepKitPersistenceManagers.Count(p => p.prefix == originalPrefix) > 1
                )
            )
            {
                needsUpdate = true;
            }

            if (needsUpdate)
            {
                string newPrefix;
                do
                {
                    maxInstanceNumber++;
                    newPrefix = $"{InstancePrefixBase}{maxInstanceNumber:D3}_"; // 001形式で生成
                } while (existingPrefixes.Contains(newPrefix)); // 生成したPrefixが既存のものと重複しないか確認

                pm.prefix = newPrefix;
                existingPrefixes.Add(newPrefix); // 新しいPrefixをセットに追加
                EditorUtility.SetDirty(pm);
                changed = true;
                Debug.Log(
                    $"[SleepKitSettingsManagerEditor] {pm.gameObject.name} の PersistenceManager の prefix を '{newPrefix}' に変更しました。"
                );
            }
            // 重複チェックのために、一度チェックした有効なPrefixも追加しておく
            else if (
                !string.IsNullOrEmpty(originalPrefix) && !existingPrefixes.Contains(originalPrefix)
            )
            {
                existingPrefixes.Add(originalPrefix);
            }
        }

        return changed;
    }

    // OnInspectorGUIは不要になったため削除
    // public override void OnInspectorGUI() { ... }
}
#endif
