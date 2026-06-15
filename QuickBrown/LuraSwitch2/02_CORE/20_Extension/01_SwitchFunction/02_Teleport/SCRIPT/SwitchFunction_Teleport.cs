
using UdonSharp;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDKBase.Editor.Attributes;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// フェード機能の使用設定
    /// </summary>
    public enum UseFadeType
    {
        /// <summary>フェード無効</summary>
        Off,
        /// <summary>フェード有効</summary>
        On
    }

    /// <summary>
    /// SwitchBase から呼ばれるフェード付きテレポート機能拡張
    /// </summary>
    public class SwitchFunction_Teleport : UdonSharpBehaviour
    {
        #region インスペクターフィールド

        [Header("■ Fade Settings")]
        [Tooltip("フェード機能の使用設定")]
        [SerializeField] private UseFadeType useFade = UseFadeType.On;

        [HelpBox("fadeInDuration：フェードインにかかる時間（秒）\nfadeOutDuration：フェードアウトにかかる時間（秒）\nteleportDelay：テレポート後の暗転維持時間（秒）")]

        [Tooltip("フェードイン時間（秒）")]
        [SerializeField] private float fadeInDuration = 0.5f;

        [Tooltip("フェードアウト時間（秒）")]
        [SerializeField] private float fadeOutDuration = 0.5f;

        [Tooltip("テレポート後の暗転維持時間（秒）")]
        [SerializeField] private float teleportDelay = 0.1f;

        [Space(10)]
        [Header("----------System（変更不要）----------")]
        [Header("■ Teleport Settings")]
        [Tooltip("テレポート先のTransform")]
        [SerializeField] private Transform teleportTarget;

        [Space(10)]
        [Header("■ Fade Objects")]
        [Tooltip("フェード用のMeshRenderer（マテリアルに_Valueプロパティが必要）")]
        [SerializeField] private MeshRenderer darkObject;

        [Tooltip("フェード用のPostProcessVolume")]
        [SerializeField] private PostProcessVolume postProcessVolume;

        [Space(10)]
        [Header("■ Audio Settings")]
        [Tooltip("テレポート時に再生するAudioSource")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("テレポート時に再生するAudioClip")]
        [SerializeField] private AudioClip audioClip;

        #endregion

        #region 定数

        private const string ValuePropertyName = "_Value";

        #endregion

        #region ランタイムフィールド

        private MaterialPropertyBlock _propertyBlock;
        private VRCPlayerApi _localPlayer;
        private float _fadeStartTime;
        private float _currentFadeDuration;
        private bool _isFading;

        #endregion

        #region Unityイベント

        /// <summary>
        /// 初期化処理
        /// </summary>
        void Start()
        {
            _localPlayer = Networking.LocalPlayer;
            _propertyBlock = new MaterialPropertyBlock();

            // 初期状態：完全に透明（_Value = 0）
            SetDarkValue(0f);
        }

        #endregion

        #region 公開API

        /// <summary>
        /// SwitchBase の External モードから呼ばれるエントリーポイント
        /// </summary>
        public void TriggerPush()
        {
            if (_isFading)
            {
                return; // すでにフェード中の場合は無視
            }

            if (_localPlayer == null)
            {
                _localPlayer = Networking.LocalPlayer;
            }

            if (_localPlayer == null || teleportTarget == null)
            {
                return;
            }

            StartFadeIn();
        }

        #endregion

        #region フェード処理

        /// <summary>
        /// フェードイン開始
        /// </summary>
        private void StartFadeIn()
        {
            _isFading = true;
            _fadeStartTime = Time.time;
            _currentFadeDuration = fadeInDuration;

            // フェードイン更新を開始
            _UpdateFadeIn();
        }

        /// <summary>
        /// フェードイン更新処理
        /// </summary>
        public void _UpdateFadeIn()
        {
            if (!_isFading)
            {
                return;
            }

            float elapsed = Time.time - _fadeStartTime;
            float t = Mathf.Clamp01(elapsed / _currentFadeDuration);

            // 0 → 1 にフェード
            SetDarkValue(t);

            if (t >= 1f)
            {
                // フェードイン完了、テレポート即座実行
                _ExecuteTeleport();
            }
            else
            {
                // 次のフレームで再度更新
                SendCustomEventDelayedSeconds(nameof(_UpdateFadeIn), 0f);
            }
        }

        /// <summary>
        /// テレポート実行処理
        /// </summary>
        public void _ExecuteTeleport()
        {
            if (_localPlayer == null || teleportTarget == null)
            {
                _isFading = false;
                return;
            }

            // プレイヤーをテレポート（位置と回転）
            _localPlayer.TeleportTo(teleportTarget.position, teleportTarget.rotation);

            // オーディオ再生
            if (audioSource != null && audioClip != null)
            {
                audioSource.PlayOneShot(audioClip);
            }

            // teleportDelay秒後にフェードアウト開始
            SendCustomEventDelayedSeconds(nameof(StartFadeOut), teleportDelay);
        }

        /// <summary>
        /// フェードアウト開始
        /// </summary>
        public void StartFadeOut()
        {
            _fadeStartTime = Time.time;
            _currentFadeDuration = fadeOutDuration;

            // フェードアウト更新を開始
            _UpdateFadeOut();
        }

        /// <summary>
        /// フェードアウト更新処理
        /// </summary>
        public void _UpdateFadeOut()
        {
            if (!_isFading)
            {
                return;
            }

            float elapsed = Time.time - _fadeStartTime;
            float t = Mathf.Clamp01(elapsed / _currentFadeDuration);

            // 1 → 0 にフェード
            SetDarkValue(1f - t);

            if (t >= 1f)
            {
                // フェードアウト完了
                _isFading = false;
            }
            else
            {
                // 次のフレームで再度更新
                SendCustomEventDelayedSeconds(nameof(_UpdateFadeOut), 0f);
            }
        }

        /// <summary>
        /// フェード値を設定
        /// </summary>
        /// <param name="value">フェード値 (0-1)</param>
        private void SetDarkValue(float value)
        {
            // フェードが Off の場合は何もしない
            if (useFade == UseFadeType.Off)
            {
                return;
            }

            // darkObject の制御
            if (darkObject != null)
            {
                if (_propertyBlock == null)
                {
                    _propertyBlock = new MaterialPropertyBlock();
                }

                darkObject.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetFloat(ValuePropertyName, value);
                darkObject.SetPropertyBlock(_propertyBlock);

                // value が 0 のときは非アクティブ、それ以外はアクティブ
                darkObject.gameObject.SetActive(value > 0f);

                // アクティブ時はプレイヤーの位置に移動
                if (value > 0f && _localPlayer != null)
                {
                    darkObject.transform.position = _localPlayer.GetPosition();
                }
            }

            // PostProcessVolume の制御
            if (postProcessVolume != null)
            {
                // Weight を設定
                postProcessVolume.weight = value;

                // Weight が 0 のときは非アクティブ、それ以外はアクティブ
                postProcessVolume.gameObject.SetActive(value > 0f);
            }
        }

        #endregion
    }
}
