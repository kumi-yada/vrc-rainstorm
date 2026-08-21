using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RBS.SleepKit2.Script
{
    /// <summary>
    /// MenuGenerator：
    /// ・シーン内の UI 全体設定をまとめたクラスです。
    /// ・ヘッダー画像の GUID（Developer Settings 用）を保持し、カスタムエディタで画像表示します。
    /// ・子孫に存在する UIAutoColorUpdater の TargetColor を targetColor に更新します（UIAutoColorIgnore が付与されている場合は除く）。
    /// ・設定した AudioSource の clip と volume を変更できます。
    /// ・ユーザー設定のメニュー項目（MenuEntry）に基づき、menuPrefab を PrefabUtility で生成してメニュー項目を自動生成します。
    /// </summary>
    public class MenuGenerator : MonoBehaviour
    {
        #region Header Settings (Developer)
        [Header("Header Settings")]
        [Tooltip("ヘッダー画像のアセット GUID。Inspector のカスタムエディタで画像を表示します。")]
        [SerializeField]
        private string headerImageGUID;
        #endregion

        #region Developer Settings (隠す)
        [Header("【Developer Settings】")]
        [Tooltip(
            "生成するメニュー項目の Prefab（Prefab 内に MenuItemAction コンポーネントが必要です）"
        )]
        [SerializeField]
        private GameObject menuPrefab;

        [Tooltip("生成したメニュー項目を配置する親 Transform（例：Canvas 内の Panel）")]
        [SerializeField]
        private Transform menuParent;

        [Tooltip("Call アクションの場合に利用する共通の移動先 Transform")]
        [SerializeField]
        private Transform callDestination;

        [Header("Script References")]
        [Tooltip("参照するスクリプト。各スクリプトのインスペクター内容が表示されます。")]
        [SerializeField]
        private MonoBehaviour[] scriptReferences;
        #endregion

        #region User Settings: Menu Entries
        [Header("Menu Entries")]
        [Tooltip(
            "各メニュー項目の設定（対象オブジェクト、アクション、表示ラベルを入力してください）"
        )]
        public System.Collections.Generic.List<MenuEntry> menuEntries =
            new System.Collections.Generic.List<MenuEntry>();

        [System.Serializable]
        public class MenuEntry
        {
            [Tooltip("対象オブジェクト")]
            public GameObject target;

            [Tooltip("実行するアクション（Call：対象を移動、Toggle：On/Off 切替）")]
            public ActionType action = ActionType.Call;

            [Tooltip("メニューに表示するラベル（空の場合は対象オブジェクトの名前が利用されます）")]
            public string label;
        }
        #endregion

        #region UI Color Settings
        [Header("UI Color Settings")]
        [Tooltip("対象の TMP_Text と Image の色を更新します。")]
        [SerializeField]
        private Color targetColor = Color.white;
        #endregion

        #region Disable Object When No Menu Entries
        [Header("Disable Settings")]
        [Tooltip("メニュー項目が 0 の場合に無効にするオブジェクト")]
        [SerializeField]
        private GameObject disableWhenNoMenuEntries;
        #endregion

        #region OnValidate と再生成処理
        private void OnValidate()
        {
            if (Application.isPlaying)
                return;
#if UNITY_EDITOR
            EditorApplication.delayCall += RegenerateAll;
#else
            RegenerateAll();
#endif
        }

        /// <summary>
        /// OnValidate 時に UI の色更新、AudioSource の更新、メニュー再生成を実施します。
        /// </summary>
        private void RegenerateAll()
        {
            if (this == null)
                return;

            // ※「同一オブジェクトではなく、子や孫に存在する UIAutoColorUpdater を全てまとめて変える」
            // 現在のオブジェクトに付いている UIAutoColorUpdater は除外し、子孫のみ対象とする
            UIAutoColorUpdater[] updaters = GetComponentsInChildren<UIAutoColorUpdater>(true);
            bool foundAny = false;
            foreach (UIAutoColorUpdater updater in updaters)
            {
                if (updater.gameObject == gameObject)
                    continue;
                updater.TargetColor = targetColor;
                foundAny = true;
            }
            if (!foundAny)
            {
                Debug.LogWarning(
                    "子孫に UIAutoColorUpdater コンポーネントが見つかりませんでした。"
                );
            }

            RegenerateMenu();
        }
        #endregion

        #region メニュー項目再生成
        private void RegenerateMenu()
        {
#if UNITY_EDITOR
            // メニュー項目が 0 の場合、disableWhenNoMenuEntries を無効化し、項目が存在する場合は有効化
            if (disableWhenNoMenuEntries != null)
            {
                disableWhenNoMenuEntries.SetActive(menuEntries.Count > 0);
            }

            if (menuPrefab == null || menuParent == null)
                return;

            // 既存のメニュー項目を全削除
            for (int i = menuParent.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(menuParent.GetChild(i).gameObject);
            }

            // 各 MenuEntry ごとに Prefab から生成し、設定を反映
            for (int i = 0; i < menuEntries.Count; i++)
            {
                MenuEntry entry = menuEntries[i];
                if (entry == null)
                    continue;

                GameObject newMenuItem =
                    PrefabUtility.InstantiatePrefab(menuPrefab, menuParent) as GameObject;
                if (newMenuItem == null)
                    continue;

                // MenuItemAction コンポーネントに各値を設定
                MenuItemAction menuAction = newMenuItem.GetComponent<MenuItemAction>();
                if (menuAction != null)
                {
                    menuAction.targetObject = entry.target;
                    menuAction.actionType = entry.action;
                    menuAction.callDestination = callDestination;
                }

                // UI のラベル更新（Prefab 内に TMP_Text コンポーネントがある前提）
                TMP_Text labelText = newMenuItem.GetComponentInChildren<TMP_Text>();
                if (labelText != null)
                {
                    if (!string.IsNullOrEmpty(entry.label))
                    {
                        labelText.text = entry.label;
                    }
                    else if (entry.target != null)
                    {
                        labelText.text = entry.target.name;
                    }
                    else
                    {
                        labelText.text = $"Item {i}";
                    }
                }
            }
#endif
        }
        #endregion
    }
}
