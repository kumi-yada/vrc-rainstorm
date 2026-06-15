
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace QuickBrown.LuraSwitch
{
    public class Slider2D_EventSender : UdonSharpBehaviour
    {
        [Header("---------------System---------------")]
        [SerializeField] private SliderSwitch targetSliderSwitch;
        [SerializeField] private Slider sourceSlider;

        // EventTriggerが多重に発火する構成に備え、PointerDown中かどうかを管理する
        private bool _isPointerDown;

        // --------------------------------------------------
        // EventTrigger から呼ぶ
        // --------------------------------------------------

        /// <summary>
        /// ポインターダウンイベント処理。EventTriggerから呼び出されます。
        /// </summary>
        public void OnPointerDown()
        {
            if (targetSliderSwitch == null)
            {
                return;
            }

            if (_isPointerDown)
            {
                return;
            }

            _isPointerDown = true;

            // TrackAreaを拡大し、PickupColliderを無効化
            targetSliderSwitch.UI_ExpandTrackAreaAndDisablePickup();

            // 先に「ドラッグ開始」扱いにする
            targetSliderSwitch.UI_OnPointerDown();

            // 1フレーム遅延してから現在値を送ることで「押した瞬間の値」を拾う。
            SendCustomEventDelayedFrames("SendCurrentValueDeferred", 1);
        }

        /// <summary>
        /// ドラッグイベント処理。EventTriggerから呼び出されます。
        /// </summary>
        public void OnDrag()
        {
            if (targetSliderSwitch == null)
            {
                return;
            }

            if (!_isPointerDown)
            {
                return;
            }

            SendCurrentValue();

            targetSliderSwitch.UI_OnDrag();
        }

        /// <summary>
        /// ポインターアップイベント処理。EventTriggerから呼び出されます。
        /// </summary>
        public void OnPointerUp()
        {
            if (targetSliderSwitch == null)
            {
                return;
            }

            if (!_isPointerDown)
            {
                return;
            }

            _isPointerDown = false;

            // PointerUp直前の最終値を送ってから確定させる
            SendCurrentValue();

            targetSliderSwitch.UI_OnPointerUp();

            // TrackAreaとPickupColliderを初期状態に戻す
            targetSliderSwitch.UI_RestoreTrackAreaAndPickup();
        }

        // --------------------------------------------------
        // Unity UI Slider の OnValueChanged(float) から呼ぶ
        // --------------------------------------------------

        /// <summary>
        /// Unity UI SliderのOnValueChangedイベントから呼び出されます。
        /// </summary>
        public void OnSliderValueChanged(float value01)
        {
            if (targetSliderSwitch == null)
            {
                return;
            }

            targetSliderSwitch.UI_OnSliderValueChanged(value01);
        }

        /// <summary>
        /// 現在のスライダー値をSliderSwitchに送信します。
        /// </summary>
        private void SendCurrentValue()
        {
            if (sourceSlider == null)
            {
                return;
            }

            targetSliderSwitch.UI_OnSliderValueChanged(sourceSlider.value);
        }

        /// <summary>
        /// 遅延実行用：現在のスライダー値をSliderSwitchに送信します。
        /// </summary>
        public void SendCurrentValueDeferred()
        {
            if (targetSliderSwitch == null)
            {
                return;
            }

            SendCurrentValue();
        }
    }
}
