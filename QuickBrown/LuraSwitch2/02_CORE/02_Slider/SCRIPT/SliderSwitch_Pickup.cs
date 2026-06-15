
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace QuickBrown.LuraSwitch
{
    public class SliderSwitch_Pickup : UdonSharpBehaviour
    {
        [Header("---------------System---------------")]
        [SerializeField] private SliderSwitch sliderSwitch;

        private VRC_Pickup _pickup;

        private void OnEnable()
        {
            _pickup = GetComponent<VRC_Pickup>();
        }

        private void Start()
        {
            _pickup = GetComponent<VRC_Pickup>();

            // Start時点で、sliderDefaultValue の位置へスライダーを揃える
            if (sliderSwitch != null)
            {
                sliderSwitch.InitializePickupToDefaultOnStart(_pickup);
            }
        }

        public override void OnPickup()
        {
            if (sliderSwitch == null)
            {
                return;
            }

            sliderSwitch.NotifyPickup(_pickup);
        }

        public override void OnDrop()
        {
            if (sliderSwitch == null)
            {
                return;
            }

            sliderSwitch.NotifyDrop(_pickup);
        }
    }
}
