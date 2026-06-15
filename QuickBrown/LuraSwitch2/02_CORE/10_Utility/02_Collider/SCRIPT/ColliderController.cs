
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDKBase.Editor.Attributes;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// SliderSwitchと連動してコライダーを上下移動させ、足場の高さを制御します。
    /// Animatorを使用してビジュアルフィードバックも提供します。
    /// </summary>
    public class ColliderController : UdonSharpBehaviour
    {
        #region インスペクターフィールド
        [Space(10)]
        [Header("■ Settings")]
        [Tooltip("スライダー値1.0時の最大移動高さ（メートル）")]
        [SerializeField][Range(0.1f, 1f)] private float heightMax = 0.5f;

        [Space(10)]
        [Header("----------System（変更不要）----------")]
        [HelpBox("JP:\n以下の設定は通常変更する必要はありません。\n\nEN:\nThe following settings do not usually need to be changed.")]
        [Header("■ References")]
        [Tooltip("基準位置となるBoxCollider")]
        [SerializeField] private BoxCollider referenceBox;

        [Tooltip("移動させる対象のコライダーオブジェクト")]
        [SerializeField] private GameObject targetCollider;

        [Tooltip("コライダーと連動して移動するCanvas（オプション）")]
        [SerializeField] private Canvas targetCanvas;

        [Header("■ Visual")]
        [Tooltip("ビジュアルフィードバック用のAnimator（オプション）")]
        [SerializeField] private Animator targetAnimator;

        [Space(10)]
        [Header("■ Runtime State")]
        [Tooltip("現在のコライダーのオフセット高さ（メートル）")]
        [SerializeField] private float colliderOffsetHeight = 0f;

        [Tooltip("現在のスライダー値（0.0～1.0）")]
        [SerializeField][Range(0f, 1f)] private float sliderValue = 0f;

        #endregion

        #region 定数

        private const string VisualOnParam = "VisualOn";
        private const string ConfirmParam = "Confirm";
        private const float VisualOffDelay = 0.2f;
        private const float VisualOffCheckDelay = 0.21f;

        #endregion

        #region ランタイムフィールド

        private float _visualOffDeadline;
        private bool _colliderEnabled = false;
        private bool _pendingStateChange = false;
        private bool _pendingState = false;

        #endregion

        #region 公開API

        /// <summary>
        /// SliderSwitchから0～1の値を受け取り、足場（Collider）の高さを調整します。
        /// 値変化中はAnimatorのVisualOnをtrueにし、一定時間更新が止まったらfalseに戻します。
        /// </summary>
        /// <param name="value01">スライダー値（0.0～1.0）</param>
        public void ApplySliderValue01(float value01)
        {
            sliderValue = Mathf.Clamp01(value01);
            ApplyOffsetFromSlider();
            NotifyVisualChanging();
        }

        /// <summary>
        /// Collider_Activatorからコライダーの有効状態のリクエストを受け取ります。
        /// 複数のActivatorから同時にリクエストが来る可能性があるため、次フレームで反映します。
        /// </summary>
        /// <param name="shouldEnable">コライダーを有効にする場合true</param>
        public void RequestColliderState(bool shouldEnable)
        {
            _pendingStateChange = true;
            _pendingState = shouldEnable;
            SendCustomEventDelayedFrames(nameof(ApplyPendingColliderState), 1);
        }

        /// <summary>
        /// 確定アニメーション（VisualOn = true）を再生します。
        /// Collider_Activatorがアクティブになった際に呼び出されます。
        /// </summary>
        public void PlayConfirmAnimation()
        {
            NotifyVisualChanging();
        }

        /// <summary>
        /// 保留中のコライダー状態を実際に適用します。
        /// </summary>
        public void ApplyPendingColliderState()
        {
            if (!_pendingStateChange)
            {
                return;
            }

            _pendingStateChange = false;
            bool newState = _pendingState;

            // 状態が変わる場合はコライダーを更新
            if (newState != _colliderEnabled)
            {
                _colliderEnabled = newState;

                if (targetCollider != null)
                {
                    targetCollider.SetActive(_colliderEnabled);
                }
            }

            // 有効化リクエストの場合は常に確定アニメーションを再生
            if (newState && targetAnimator != null && targetAnimator.enabled && targetAnimator.gameObject.activeInHierarchy && targetAnimator.runtimeAnimatorController != null)
            {
                targetAnimator.SetTrigger(ConfirmParam);
            }
        }

        #endregion

        #region ビジュアル管理

        /// <summary>
        /// ビジュアル変更中であることをAnimatorに通知します。
        /// </summary>
        private void NotifyVisualChanging()
        {
            if (targetAnimator == null || !targetAnimator.enabled || !targetAnimator.gameObject.activeInHierarchy || targetAnimator.runtimeAnimatorController == null)
            {
                return;
            }

            // 変更中は ON
            targetAnimator.SetBool(VisualOnParam, true);

            // 停止判定：一定時間更新が無ければ OFF
            float now = Time.time;
            _visualOffDeadline = now + VisualOffDelay;
            SendCustomEventDelayedSeconds(nameof(ApplyVisualOffIfStopped), VisualOffCheckDelay);
        }

        /// <summary>
        /// スライダー値の更新が停止していればビジュアルをOFFにします。
        /// まだ更新が続いている場合は再チェックを予約します。
        /// </summary>
        public void ApplyVisualOffIfStopped()
        {
            if (targetAnimator == null || !targetAnimator.enabled || !targetAnimator.gameObject.activeInHierarchy || targetAnimator.runtimeAnimatorController == null)
            {
                return;
            }

            float now = Time.time;
            if (now + 0.0001f < _visualOffDeadline)
            {
                // まだ更新が続いているので、期限まで待って再チェック
                float wait = _visualOffDeadline - now;
                SendCustomEventDelayedSeconds(nameof(ApplyVisualOffIfStopped), Mathf.Max(0.01f, wait));
                return;
            }

            targetAnimator.SetBool(VisualOnParam, false);
        }

        #endregion

        #region 位置制御

        /// <summary>
        /// スライダー値に基づいてコライダーとCanvasの位置を更新します。
        /// </summary>
        private void ApplyOffsetFromSlider()
        {
            float t = Mathf.Clamp01(sliderValue);
            float max = Mathf.Max(0f, heightMax);

            // 実際に動かす高さ（m）
            colliderOffsetHeight = t * max;

            if (referenceBox == null) return;
            if (targetCollider == null) return;

            Transform referenceTransform = referenceBox.transform;
            if (referenceTransform == null) return;

            Transform targetTransform = targetCollider.transform;
            if (targetTransform == null) return;

            // まず XZ を基準位置へ合わせ、Y だけオフセットして上下移動
            Vector3 basePos = referenceTransform.position;
            Vector3 newPos = new Vector3(basePos.x, basePos.y + colliderOffsetHeight, basePos.z);

            // Canvasも連動させるため、移動量を保持してから反映する
            Vector3 oldPos = targetTransform.position;
            Vector3 delta = newPos - oldPos;

            targetTransform.position = newPos;

            if (targetCanvas != null)
            {
                Transform canvasTransform = targetCanvas.transform;
                if (canvasTransform != null)
                {
                    canvasTransform.position += delta;
                }
            }
        }

        #endregion

        #region エディタサポート

        /// <summary>
        /// インスペクター値変更時の処理
        /// </summary>
        private void OnValidate()
        {
            ApplyOffsetFromSlider();
        }

        #endregion
    }
}
