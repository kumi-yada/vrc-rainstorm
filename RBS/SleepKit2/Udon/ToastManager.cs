using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace RBS.SleepKit2.Udon
{
    public class ToastManager : UdonSharpBehaviour
    {
        [Header("Toast Settings")]
        [Tooltip("トーストとして表示するGameObject")]
        public GameObject toastObject;

        [Tooltip("トースト内のタイトルテキスト")]
        public TMP_Text titleText;

        [Tooltip("トースト内のメッセージテキスト")]
        public TMP_Text messageText;

        [Tooltip("トーストの透明度を制御するCanvasGroup")]
        public CanvasGroup canvasGroup;

        [Tooltip("トーストの進捗バーとして使用するImage")]
        public Image progressBar;

        [Header("Animation Settings")]
        [Tooltip("フェードインのステップ数")]
        public int fadeInSteps = 10;

        [Tooltip("フェードアウトのステップ数")]
        public int fadeOutSteps = 10;

        [Tooltip("フェードインの総時間（秒）")]
        public float fadeInDuration = 0.5f;

        [Tooltip("フェードアウトの総時間（秒）")]
        public float fadeOutDuration = 0.5f;

        [Tooltip("トースト表示後の保持時間（秒）")]
        public float displayDuration = 10f;

        [Header("Progress Bar Settings")]
        [Tooltip("進捗バーのステップ数")]
        public int progressSteps = 20;

        [Tooltip("進捗バーのステップごとの遅延時間（秒）")]
        public float progressStepDelay = 0.5f; // displayDuration / progressSteps = 10 / 20 = 0.5秒

        // 状態管理
        private const int STATE_IDLE = 0;
        private const int STATE_FADING_IN = 1;
        private const int STATE_VISIBLE = 2;
        private const int STATE_FADING_OUT = 3;

        private int currentState = STATE_IDLE;

        // アニメーションステップの遅延時間と透明度増減量
        private float fadeInStepDelay;
        private float fadeOutStepDelay;

        private float fadeInStepAmount;
        private float fadeOutStepAmount;

        // 進捗バーのステップごとの幅減少量
        private float progressStepAmount;

        // 最大幅の保存
        private float maxProgressWidth;

        // 進捗バーの現在のステップ数
        private int currentProgressStep = 0;

        // フェードアウト後に表示するトーストの内容を保持
        private string pendingToastTitle = "";
        private string pendingToastMessage = "";

        void Start()
        {
            if (
                toastObject == null
                || titleText == null
                || messageText == null
                || canvasGroup == null
                || progressBar == null
            )
            {
                Debug.LogError("ToastManager: 必要なコンポーネントが設定されていません。");
                return;
            }

            // 初期状態を設定
            toastObject.SetActive(false);
            canvasGroup.alpha = 0f;
            progressBar.rectTransform.sizeDelta = new Vector2(
                progressBar.rectTransform.sizeDelta.x,
                progressBar.rectTransform.sizeDelta.y
            );

            // ステップごとの遅延時間と透明度増減量を計算
            fadeInStepDelay = fadeInDuration / fadeInSteps;
            fadeInStepAmount = 1f / fadeInSteps;

            fadeOutStepDelay = fadeOutDuration / fadeOutSteps;
            fadeOutStepAmount = 1f / fadeOutSteps;

            // 進捗バーの初期設定
            maxProgressWidth = progressBar.rectTransform.sizeDelta.x;
            progressStepAmount = maxProgressWidth / progressSteps;
            progressBar.rectTransform.sizeDelta = new Vector2(
                maxProgressWidth,
                progressBar.rectTransform.sizeDelta.y
            );
        }

        /// <summary>
        /// トーストを表示するメソッド
        /// </summary>
        /// <param name="title">トーストのタイトル</param>
        /// <param name="message">トーストのメッセージ</param>
        public void ShowToast(string title, string message)
        {
            if (currentState == STATE_FADING_IN || currentState == STATE_VISIBLE)
            {
                // 現在表示中の場合、トーストを即座にフェードアウトさせてから新しいトーストを表示
                pendingToastTitle = title;
                pendingToastMessage = message;

                // 状態をフェードアウトに変更
                currentState = STATE_FADING_OUT;

                // フェードアウト開始
                SendCustomEventDelayedSeconds(nameof(FadeOutStep), fadeOutStepDelay);
            }
            else
            {
                // 新しいトーストを設定してフェードイン開始
                pendingToastTitle = "";
                pendingToastMessage = "";

                titleText.text = title;
                messageText.text = message;

                toastObject.SetActive(true);
                canvasGroup.alpha = 0f;

                // 進捗バーをリセット
                progressBar.rectTransform.sizeDelta = new Vector2(
                    maxProgressWidth,
                    progressBar.rectTransform.sizeDelta.y
                );
                currentProgressStep = 0;

                // フェードイン開始
                currentState = STATE_FADING_IN;
                SendCustomEventDelayedSeconds(nameof(FadeInStep), fadeInStepDelay);
            }
        }

        /// <summary>
        /// トーストを非表示にするメソッド
        /// </summary>
        public void HideToast()
        {
            if (currentState == STATE_VISIBLE)
            {
                // フェードアウト開始
                currentState = STATE_FADING_OUT;
                SendCustomEventDelayedSeconds(nameof(FadeOutStep), fadeOutStepDelay);
            }
        }

        /// <summary>
        /// フェードインの各ステップ
        /// </summary>
        public void FadeInStep()
        {
            if (currentState != STATE_FADING_IN)
                return;

            canvasGroup.alpha += fadeInStepAmount;
            if (canvasGroup.alpha >= 1f)
            {
                canvasGroup.alpha = 1f;
                currentState = STATE_VISIBLE;

                // 進捗バーのアニメーション開始
                StartProgressBar();

                // displayDuration秒後にフェードアウトを開始
                SendCustomEventDelayedSeconds(nameof(HideToast), displayDuration);
            }
            else
            {
                // 次のフェードインステップをスケジュール
                SendCustomEventDelayedSeconds(nameof(FadeInStep), fadeInStepDelay);
            }
        }

        /// <summary>
        /// フェードアウトの各ステップ
        /// </summary>
        public void FadeOutStep()
        {
            if (currentState != STATE_FADING_OUT)
                return;

            canvasGroup.alpha -= fadeOutStepAmount;
            if (canvasGroup.alpha <= 0f)
            {
                canvasGroup.alpha = 0f;
                currentState = STATE_IDLE;
                toastObject.SetActive(false);

                // 進捗バーをリセット
                progressBar.rectTransform.sizeDelta = new Vector2(
                    maxProgressWidth,
                    progressBar.rectTransform.sizeDelta.y
                );
                currentProgressStep = 0;

                // 保留中のトーストがあれば表示
                if (
                    !string.IsNullOrEmpty(pendingToastTitle)
                    || !string.IsNullOrEmpty(pendingToastMessage)
                )
                {
                    string title = pendingToastTitle;
                    string message = pendingToastMessage;
                    pendingToastTitle = "";
                    pendingToastMessage = "";
                    ShowToast(title, message);
                }
            }
            else
            {
                // 次のフェードアウトステップをスケジュール
                SendCustomEventDelayedSeconds(nameof(FadeOutStep), fadeOutStepDelay);
            }
        }

        /// <summary>
        /// 進捗バーのアニメーションを開始する
        /// </summary>
        private void StartProgressBar()
        {
            currentProgressStep = 0;
            SendCustomEventDelayedSeconds(nameof(ProgressBarStep), progressStepDelay);
        }

        /// <summary>
        /// 進捗バーの各ステップ
        /// </summary>
        public void ProgressBarStep()
        {
            if (currentState != STATE_VISIBLE)
                return;

            currentProgressStep++;
            if (currentProgressStep >= progressSteps)
            {
                // 進捗バーが完了
                progressBar.rectTransform.sizeDelta = new Vector2(
                    0f,
                    progressBar.rectTransform.sizeDelta.y
                );
            }
            else
            {
                // 進捗バーの幅を減少
                float newWidth = maxProgressWidth - (progressStepAmount * currentProgressStep);
                progressBar.rectTransform.sizeDelta = new Vector2(
                    newWidth,
                    progressBar.rectTransform.sizeDelta.y
                );

                // 次の進捗バーステップをスケジュール
                SendCustomEventDelayedSeconds(nameof(ProgressBarStep), progressStepDelay);
            }
        }
    }
}
