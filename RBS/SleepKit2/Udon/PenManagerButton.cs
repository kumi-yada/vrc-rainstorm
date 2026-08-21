using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace RBS.SleepKit2.Script
{
    /// <summary>
    /// ・エディタ拡張側から直接参照を設定することを前提としています。
    /// ・ペンの参照は QvPen_Pen 型ではなく GameObject 型として保持します（QvPen_Pen が存在しない場合に対応）。
    /// ・ボタン押下時に、設定されたペンのオブジェクトの Transform を、
    ///   設定されたテレポート先の位置・回転に更新します。
    /// </summary>
    public class PenManagerButton : UdonSharpBehaviour
    {
        [
            SerializeField,
            Tooltip("テレポート対象のペンの GameObject。エディタ拡張側から設定します。")
        ]
        private GameObject penObject;

        [SerializeField, Tooltip("テレポート先の Transform。エディタ拡張側から設定します。")]
        private Transform teleportTarget;

        private Button button;

        private void Start()
        {
            // ボタンの初期化は、エディタ側から設定が完了した後に実行される前提です。
            button = GetComponent<Button>();
            // onClick イベントは、インスペクターから設定されるか、
            // UdonBehaviour 内で OnButtonClicked() を呼び出す形で動作します。
        }

        /// <summary>
        /// エディタ拡張側からペンの GameObject を設定するためのメソッド。
        /// QvPen_Pen 型ではなく、GameObject 型で受け取ります。
        /// </summary>
        public void SetPen(GameObject newPenObject)
        {
            penObject = newPenObject;
        }

        /// <summary>
        /// エディタ拡張側からテレポート先を設定するためのメソッド。
        /// </summary>
        public void SetTeleportTarget(Transform newTarget)
        {
            teleportTarget = newTarget;
        }

        /// <summary>
        /// ボタン押下時の処理。
        /// ペンのオブジェクトが設定されていれば、その Transform をテレポート先の位置・回転に変更します。
        /// </summary>
        public void OnButtonClicked()
        {
            if (penObject == null)
            {
                Debug.LogError("[PenManagerButton] penObject が設定されていません。");
                return;
            }
            if (teleportTarget == null)
            {
                Debug.LogError("[PenManagerButton] teleportTarget が設定されていません。");
                return;
            }

            Networking.SetOwner(Networking.LocalPlayer, penObject);
            Networking.SetOwner(Networking.LocalPlayer, penObject.transform.parent.gameObject);
            penObject.transform.position = teleportTarget.position;
            penObject.transform.rotation = teleportTarget.rotation;
        }
    }
}
