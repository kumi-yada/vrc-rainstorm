
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Dynamics;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// スイッチのインタラクション処理を担当するトリガークラスです。
    /// </summary>
    public class Switch_Trigger : UdonSharpBehaviour
    {
        [SerializeField] private SwitchBase targetSwitch;

        /// <summary>
        /// ターゲットとなるスイッチを取得します。
        /// </summary>
        public SwitchBase TargetSwitch => targetSwitch;

        /// <summary>
        /// ターゲットスイッチを設定します。
        /// </summary>
        /// <param name="target">設定するスイッチ</param>
        public void Configure(SwitchBase target)
        {
            targetSwitch = target;
        }

        /// <summary>
        /// インタラクト時の処理を実行します。ターゲットスイッチのUI_Clickを呼び出します。
        /// </summary>
        public override void Interact()
        {
            if (targetSwitch == null)
            {
                return;
            }

            targetSwitch.UI_Click();
        }

        /// <summary>
        /// Physbone接触イベントを取得します。
        /// </summary>
        public override void OnContactEnter(ContactEnterInfo contactInfo)
        {
            if (targetSwitch == null)
            {
                return;
            }

            if (contactInfo.contactSender.player.isLocal)
            {
                targetSwitch.UI_Click();
            }
        }
    }
}
