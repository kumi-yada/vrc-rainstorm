
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using QuickBrown.LuraSwitch;
using VRC.SDKBase.Editor.Attributes;


namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// GameObjectのアクティブ状態に応じてミラーの品質を自動制御します。
    /// 複数のActivatorが有効な場合、最も高い品質要求を採用します（HQ > LQ）。
    /// </summary>
    public class MirrorActivator : UdonSharpBehaviour
    {
        #region インスペクターフィールド

        [Header("---------------System---------------")]
        [SerializeField] private MirrorController mirrorController;

        [Tooltip("アクティブ化でトリガーするモードです。1=Low Quality、2=High Quality")]
        [SerializeField] private int mirrorTypeMode = 1;

        [Tooltip("連動する他のActivatorを設定します。")]
        [HelpBox("JP:\nActivatorをすべて指定する必要があります\n\nEN:\nAll Activators must be specified", HelpBoxAttribute.MessageType.Info)]
        [SerializeField] private MirrorActivator[] activatorGroup;



        #endregion

        #region Unityイベント

        /// <summary>
        /// このコンポーネントが有効化された際に呼ばれます。
        /// </summary>
        private void OnEnable()
        {
            UpdateMirrorFromAllActivators();
        }

        /// <summary>
        /// このコンポーネントが無効化された際に呼ばれます。
        /// </summary>
        private void OnDisable()
        {
            UpdateMirrorFromAllActivators();
        }

        #endregion

        #region ミラー制御

        /// <summary>
        /// 全てのActivatorの状態を評価し、ミラーの品質を更新します。
        /// 同じMirrorControllerを参照する全Activatorの中で最も高い品質要求を採用します。
        /// </summary>
        private void UpdateMirrorFromAllActivators()
        {
            if (mirrorController == null)
            {
                return;
            }

            // 同じ MirrorController を参照している有効な MirrorActivator の中で、
            // 最も強い要求（HQ=2 > LQ=1）を採用する。
            int strongestMode = 0; // 0=Off

            // Udonでは FindObjectsOfType(includeInactive) が使えないため、Inspectorでグループを指定して判定する
            EvaluateActivator(this, ref strongestMode);
            if (activatorGroup != null)
            {
                for (int i = 0; i < activatorGroup.Length; i++)
                {
                    EvaluateActivator(activatorGroup[i], ref strongestMode);
                    if (strongestMode == 2)
                    {
                        break;
                    }
                }
            }

            MirrorType targetType = MirrorType.Off;
            if (strongestMode == 1) targetType = MirrorType.LQ;
            else if (strongestMode == 2) targetType = MirrorType.HQ;

            // 同フレーム内に複数のActivatorがON/OFFされると、中間状態（HQ→LQ→Off等）を経由してしまうため、
            // MirrorController側で次フレームにまとめて反映する。
            mirrorController.RequestMirrorType(targetType);
        }

        #endregion

        #region ユーティリティ

        /// <summary>
        /// 指定されたActivatorの状態を評価し、最も強い品質要求を更新します。
        /// </summary>
        /// <param name="activator">評価対象のActivator</param>
        /// <param name="strongestMode">現在の最も強い品質要求（参照渡し）</param>
        private void EvaluateActivator(MirrorActivator activator, ref int strongestMode)
        {
            if (activator == null) return;
            if (activator.mirrorController != mirrorController) return;

            // 要求が有効になる条件：コンポーネントが有効 かつ GameObject が有効
            if (!activator.enabled) return;
            if (!activator.gameObject.activeInHierarchy) return;

            int mode = activator.mirrorTypeMode;
            if (mode < 1) mode = 1;
            if (mode > 2) mode = 2;

            if (mode > strongestMode)
            {
                strongestMode = mode;
            }
        }

        #endregion
    }
}
