using System.Text.RegularExpressions;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif


namespace RBS.SleepKit2.Script
{
    public class PersistenceManagerUpdater : ComponentDetectorBase
    {
        private static readonly Regex s_numberPrefixPattern = new Regex(@"^\[\d{3}\]_");
#if UNITY_EDITOR
        private static bool isSavingScene = false;
#endif

        public override void Refresh()
        {
            PersistenceManager[] managers = FindObjectsOfType<PersistenceManager>();
            System.Array.Sort(managers, (a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
            for (int i = 0; i < managers.Length; i++)
            {
                // prefix が "[NNN]_" で始まっていたら取り除く
                string currentPrefix = managers[i].prefix;
                if (s_numberPrefixPattern.IsMatch(currentPrefix))
                {
                    currentPrefix = s_numberPrefixPattern.Replace(currentPrefix, "");
                }

                // 新しい番号 "[001]_" を先頭に付与
                string numberedPrefix = $"[{(i + 1).ToString("D3")}]_{currentPrefix}";
                managers[i].prefix = numberedPrefix;

#if UNITY_EDITOR
                // managers[i] とシーンを Dirty にする
                EditorUtility.SetDirty(managers[i]);
                EditorSceneManager.MarkSceneDirty(managers[i].gameObject.scene);

                // シーン保存の再入を防ぐ
                if (!isSavingScene)
                {
                    isSavingScene = true;
                    // シーンを保存
                    if (EditorSceneManager.SaveScene(managers[i].gameObject.scene))
                    {
                        Debug.Log("[PersistenceUpdater] シーンの保存に成功しました。");
                    }
                    else
                    {
                        Debug.LogWarning("[PersistenceUpdater] シーンの保存に失敗しました。");
                    }
                    isSavingScene = false;
                }
#endif
            }
        }
    }
}
