using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RBS.SleepKit2.Script
{
    /// <summary>
    /// 親オブジェクト配下の TMP_Text と Image の色を Inspector の targetColor に更新します。
    /// ただし、対象オブジェクトまたはその祖先に UIAutoColorIgnore が付いている場合は色変更を無視します。
    /// </summary>
    public class UIAutoColorUpdater : MonoBehaviour, IEditorOnly
    {
        [SerializeField]
        private Color targetColor = Color.white;

        /// <summary>
        /// Inspector上で指定する色です。この色に対象の TMP_Text と Image の色を変更します。
        /// </summary>
        public Color TargetColor
        {
            get { return targetColor; }
            set
            {
                if (targetColor != value)
                {
                    targetColor = value;
                    UpdateChildColors();
                }
            }
        }

        // Inspector上で値が変更された際に自動的に呼ばれる
        private void OnValidate()
        {
            UpdateChildColors();
        }

        /// <summary>
        /// 親オブジェクト配下の TMP_Text と Image の色を targetColor に変更します。
        /// UIAutoColorIgnore が付いているオブジェクトやその子孫は対象外とします。
        /// </summary>
        private void UpdateChildColors()
        {
            // 親が存在しない場合は何もしない
            if (transform.parent == null)
                return;

            // TMP_Text コンポーネントを全て取得
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
            {
                // 対象オブジェクトまたはその祖先に UIAutoColorIgnore が付いている場合はスキップ
                if (ShouldIgnore(text.transform))
                    continue;

                text.color = targetColor;
#if UNITY_EDITOR
                EditorUtility.SetDirty(text);
#endif
            }

            // Image コンポーネントを全て取得
            Image[] images = GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                // 対象オブジェクトまたはその祖先に UIAutoColorIgnore が付いている場合はスキップ
                if (ShouldIgnore(image.transform))
                    continue;

                image.color = targetColor;
#if UNITY_EDITOR
                EditorUtility.SetDirty(image);
#endif
            }
        }

        /// <summary>
        /// 対象の Transform またはその祖先に UIAutoColorIgnore が付いているかどうかを判定します。
        /// </summary>
        /// <param name="t">チェック対象の Transform</param>
        /// <returns>UIAutoColorIgnore があれば true、なければ false</returns>
        private bool ShouldIgnore(Transform t)
        {
            return t.GetComponentInParent<UIAutoColorIgnore>(true) != null;
        }
    }
}
