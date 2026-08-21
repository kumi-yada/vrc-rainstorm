using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace RBS.SleepKit2.Script
{
    /// <summary>
    /// 検出器の基底クラスです。
    /// シーン内に特定のコンポーネントが存在するかどうかに基づいて、
    /// 対象の GameObject のアクティブ状態およびタグを更新する機能を提供します。
    /// 各更新処理は個別の配列で管理され、個別に処理されます。
    /// 派生クラスは <see cref="Refresh"/> メソッドを実装して検出条件を判断してください。
    /// </summary>
    [ExecuteAlways]
    public abstract class ComponentDetectorBase : MonoBehaviour, IEditorOnly
    {
        /// <summary>
        /// アクティブ状態更新の対象となる GameObject のリスト。
        /// </summary>
        [SerializeField]
        protected List<GameObject> activeStateTargets = new List<GameObject>();

        /// <summary>
        /// タグ更新の対象となる GameObject のリスト。
        /// </summary>
        [SerializeField]
        protected List<GameObject> editorOnlyTagTargets = new List<GameObject>();

        /// <summary>
        /// 前回の検出結果を保持します（無駄な更新を防止するための参考用）。
        /// </summary>
        protected bool previousDetectionState = false;

        /// <summary>
        /// シーン内の特定コンポーネントの存在状況に基づいて、
        /// 対象の GameObject の状態（アクティブ状態およびタグ）を更新します。
        /// このメソッドはシーン保存時などに必ず呼び出されます。
        /// </summary>
        public abstract void Refresh();

        /// <summary>
        /// 検出結果に応じて、アクティブ状態更新対象の GameObject の状態を更新します。
        /// detectionResult が true ならオブジェクトを有効に、false なら無効に設定します。
        /// </summary>
        /// <param name="detectionResult">検出結果</param>
        protected void SetActiveStateForTargets(bool detectionResult)
        {
            if (activeStateTargets == null)
                return;

            foreach (GameObject obj in activeStateTargets)
            {
                if (obj == null)
                    continue;
                obj.SetActive(detectionResult);
            }
        }

        /// <summary>
        /// 検出結果に応じて、タグ更新対象の GameObject のタグを更新します。
        /// detectionResult が false なら "EditorOnly"、true なら "Untagged" に設定します。
        /// </summary>
        /// <param name="detectionResult">検出結果</param>
        protected void SetTagForTargets(bool detectionResult)
        {
            if (editorOnlyTagTargets == null)
                return;

            foreach (GameObject obj in editorOnlyTagTargets)
            {
                if (obj == null)
                    continue;

                if (!detectionResult)
                {
                    if (!obj.CompareTag("EditorOnly"))
                    {
                        obj.tag = "EditorOnly";
                    }
                }
                else
                {
                    obj.tag = "Untagged";
                }
            }
        }
    }
}
