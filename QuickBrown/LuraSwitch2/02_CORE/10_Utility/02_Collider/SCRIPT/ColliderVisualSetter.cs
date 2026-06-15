using UnityEngine;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// BoxColliderの天面にCanvasをフィット配置します（エディタでも動作）。
    /// 変更検知により必要なときだけ更新する設計です。
    /// </summary>
    [ExecuteAlways]
    public class ColliderVisualSetter : MonoBehaviour
    {
        #region インスペクターフィールド

        [Tooltip("配置の基準となるBoxColliderです。")]
        [SerializeField] private BoxCollider ReferenceBox;

        [Tooltip("BoxColliderの天面に配置するCanvasです。")]
        [SerializeField] private Canvas targetCanvas;

        [Header("Z-Fight Offset")]
        [Tooltip("Canvas をワールドY+へ持ち上げてZ-fightを回避します（単位: m）")]
        [SerializeField] private float CanvasWorldYOffset = 0.002f;

        [Tooltip("ReferenceBoxに合わせて配置・スケールするコライダーオブジェクトです。")]
        [SerializeField] private GameObject targetCollider;

        #endregion

        #region トラッキング状態

        /// <summary>
        /// BoxColliderの変更を検知するための状態を保持する構造体です。
        /// </summary>
        private struct TrackingState
        {
            public Vector3 lastPosition;
            public Quaternion lastRotation;
            public Vector3 lastLossyScale;
            public Vector3 lastColliderCenter;
            public Vector3 lastColliderSize;
            public float lastCanvasWorldYOffset;
            public bool isInitialized;
        }

        private TrackingState _track;

        #endregion

        #region 公開API

        /// <summary>
        /// 外部（Editorなど）から明示的に更新したいときに呼びます。
        /// </summary>
        public void RefreshNow()
        {
            ApplyVisualsNow();
        }

        #endregion

        #region Unityイベント

        /// <summary>
        /// コンポーネントが有効化された際に呼ばれます。
        /// </summary>
        private void OnEnable()
        {
            InitializeTracking(ref _track, ReferenceBox);
            _track.lastCanvasWorldYOffset = CanvasWorldYOffset;
            ApplyVisualsNow();
        }

        /// <summary>
        /// 毎フレーム更新後に呼ばれます。変更検知してビジュアルを更新します。
        /// </summary>
        private void LateUpdate()
        {
            if (ReferenceBox == null) return;

            if (!_track.isInitialized)
            {
                InitializeTracking(ref _track, ReferenceBox);
                _track.lastCanvasWorldYOffset = CanvasWorldYOffset;
                ApplyVisualsNow();
                return;
            }

            if (HasBoxChanged(ref _track, ReferenceBox, CanvasWorldYOffset))
            {
                ApplyVisualsNow();
            }
        }

        #endregion

        #region ビジュアル適用

        /// <summary>
        /// ビジュアル（Collider、Canvas）を即座に適用します。
        /// </summary>
        private void ApplyVisualsNow()
        {
            ApplyTargetColliderToReferenceBox();
            ApplyCanvasToColliderTop();
        }

        /// <summary>
        /// ターゲットコライダーをReferenceBoxに合わせて配置・スケールします。
        /// </summary>
        private void ApplyTargetColliderToReferenceBox()
        {
            if (ReferenceBox == null) return;
            if (targetCollider == null) return;

            var referenceTransform = ReferenceBox.transform;
            if (referenceTransform == null) return;

            var targetTransform = targetCollider.transform;
            if (targetTransform == null) return;

            // 位置・回転：ReferenceBox の Transform に一致
            targetTransform.SetPositionAndRotation(referenceTransform.position, referenceTransform.rotation);

            // XZ スケール：BoxCollider.size（ローカル）× lossyScale（ワールド）から算出
            Vector3 sizeWorld = Vector3.Scale(ReferenceBox.size, referenceTransform.lossyScale);
            float sizeX = Mathf.Abs(sizeWorld.x);
            float sizeZ = Mathf.Abs(sizeWorld.z);

            // 親のスケールがある場合は、ローカルスケールへ戻して設定
            Vector3 parentLossyScale = Vector3.one;
            if (targetTransform.parent != null)
            {
                parentLossyScale = targetTransform.parent.lossyScale;
            }

            float parentX = Mathf.Abs(parentLossyScale.x);
            float parentZ = Mathf.Abs(parentLossyScale.z);
            float localX = parentX > 0.00001f ? sizeX / parentX : sizeX;
            float localZ = parentZ > 0.00001f ? sizeZ / parentZ : sizeZ;

            Vector3 localScale = targetTransform.localScale;
            localScale.x = localX;
            localScale.z = localZ;
            targetTransform.localScale = localScale;
        }

        /// <summary>
        /// CanvasをBoxColliderの底面に合わせて配置します。
        /// </summary>
        private void ApplyCanvasToColliderTop()
        {
            if (ReferenceBox == null) return;
            if (targetCanvas == null) return;

            var colliderTransform = ReferenceBox.transform;
            if (colliderTransform == null) return;

            RectTransform rectTransform = targetCanvas.GetComponent<RectTransform>();
            if (rectTransform == null) return;

            // BoxCollider.size はローカル値なので lossyScale を反映してワールド寸法にする
            Vector3 sizeWorld = Vector3.Scale(ReferenceBox.size, colliderTransform.lossyScale);
            float sizeX = Mathf.Abs(sizeWorld.x);
            float sizeY = Mathf.Abs(sizeWorld.y);
            float sizeZ = Mathf.Abs(sizeWorld.z);

            Vector3 centerWorld = colliderTransform.TransformPoint(ReferenceBox.center);
            Vector3 bottomCenterWorld = centerWorld - colliderTransform.up * (sizeY * 0.5f);

            // Z-fight回避：ワールドY+へ持ち上げ
            if (Mathf.Abs(CanvasWorldYOffset) > 0.0000001f)
            {
                bottomCenterWorld += Vector3.up * CanvasWorldYOffset;
            }

            // 仕様：colliderスケールが (2,1,4) の場合、Canvas は Width=4, Height=2
            // => Width は Z（forward方向）、Height は X（right方向）に対応させる
            rectTransform.sizeDelta = new Vector2(sizeZ, sizeX);

            // Scale は 1:1:1（sizeDelta に実寸を持たせる）
            rectTransform.localScale = Vector3.one;

            // 回転：Canvas の面を底面（法線=-up）に合わせる
            // forward(ローカルZ)=-up になるように向ける
            Vector3 worldForward = -colliderTransform.up;
            Vector3 worldUp = -colliderTransform.right;
            Quaternion canvasRotation = Quaternion.LookRotation(worldForward, worldUp);

            rectTransform.SetPositionAndRotation(bottomCenterWorld, canvasRotation);
        }

        #endregion

        #region トラッキング管理

        /// <summary>
        /// トラッキング状態を初期化します。
        /// </summary>
        /// <param name="state">初期化するトラッキング状態</param>
        /// <param name="box">対象のBoxCollider</param>
        private static void InitializeTracking(ref TrackingState state, BoxCollider box)
        {
            if (box == null)
            {
                state.isInitialized = false;
                return;
            }

            var t = box.transform;
            if (t == null)
            {
                state.isInitialized = false;
                return;
            }

            state.lastPosition = t.position;
            state.lastRotation = t.rotation;
            state.lastLossyScale = t.lossyScale;
            state.lastColliderCenter = box.center;
            state.lastColliderSize = box.size;
            state.lastCanvasWorldYOffset = 0f;
            state.isInitialized = true;
        }

        /// <summary>
        /// BoxColliderに変更があったかを判定します。
        /// </summary>
        /// <param name="state">トラッキング状態</param>
        /// <param name="box">対象のBoxCollider</param>
        /// <param name="canvasWorldYOffset">CanvasのワールドY軸方向のオフセット</param>
        /// <returns>変更があった場合true</returns>
        private static bool HasBoxChanged(ref TrackingState state, BoxCollider box, float canvasWorldYOffset)
        {
            if (box == null) return false;
            var t = box.transform;
            if (t == null) return false;

            bool changed = false;

            if (Vector3.Distance(t.position, state.lastPosition) > 0.0001f)
            {
                state.lastPosition = t.position;
                changed = true;
            }

            if (Quaternion.Angle(t.rotation, state.lastRotation) > 0.0001f)
            {
                state.lastRotation = t.rotation;
                changed = true;
            }

            if (Vector3.Distance(t.lossyScale, state.lastLossyScale) > 0.0001f)
            {
                state.lastLossyScale = t.lossyScale;
                changed = true;
            }

            if (Vector3.Distance(box.center, state.lastColliderCenter) > 0.0001f)
            {
                state.lastColliderCenter = box.center;
                changed = true;
            }

            if (Vector3.Distance(box.size, state.lastColliderSize) > 0.0001f)
            {
                state.lastColliderSize = box.size;
                changed = true;
            }

            if (Mathf.Abs(canvasWorldYOffset - state.lastCanvasWorldYOffset) > 0.0001f)
            {
                state.lastCanvasWorldYOffset = canvasWorldYOffset;
                changed = true;
            }

            return changed;
        }

        #endregion

        #region エディタサポート

        /// <summary>
        /// エディタでの値の変更時に呼ばれる検証処理です。
        /// </summary>
        private void OnValidate()
        {
            InitializeTracking(ref _track, ReferenceBox);
            _track.lastCanvasWorldYOffset = CanvasWorldYOffset;
            ApplyVisualsNow();
        }

        #endregion
    }
}
