
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace QuickBrown.LuraSwitch
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SwitchBoard_Trigger : UdonSharpBehaviour
    {
        [Header("■ 基本設定／Basic Settings")]
        [Tooltip("このTriggerが操作するHolder(UdonBehaviour)です。Inspectorで設定してください")]
        [SerializeField] private UdonBehaviour holderUdon;

        public override void Interact()
        {
            if (holderUdon == null)
            {
                return;
            }

            holderUdon.SendCustomEvent("Trigger_SnapBoardHere");
        }
    }
}
