using UnityEngine;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// BoxColliderの底面プレビューを可視化します（エディタでも動作）。
    /// </summary>
    [ExecuteAlways]
    public class Collider_AreaPreview : MonoBehaviour
    {
        #region インスペクターフィールド

        [Tooltip("プレビュー対象のBoxColliderです。")]
        [SerializeField] private BoxCollider ReferenceBox;

        [Header("Z-Fight Offset")]
        [Tooltip("プレビューをワールドY+へ持ち上げてZ-fightを回避します（単位: m）")]
        [SerializeField] private float WorldYOffset = 0.002f;

        [Header("Bottom Face")]
        [Tooltip("底面全体を表示するプレーンオブジェクトです。")]
        [SerializeField] private GameObject BottomFace;

        [Header("Bottom Face - Center")]
        [Tooltip("底面の中心点を表示するオブジェクトです。")]
        [SerializeField] private GameObject BottomCenter;

        [Header("Bottom Face - Edges")]
        [Tooltip("底面の右エッジを表示するオブジェクトです。")]
        [SerializeField] private GameObject BottomEdgeRight;

        [Tooltip("底面の左エッジを表示するオブジェクトです。")]
        [SerializeField] private GameObject BottomEdgeLeft;

        [Tooltip("底面の前エッジを表示するオブジェクトです。")]
        [SerializeField] private GameObject BottomEdgeFront;

        [Tooltip("底面の後エッジを表示するオブジェクトです。")]
        [SerializeField] private GameObject BottomEdgeBack;

        [Header("Bottom Face - Corners")]
        [Tooltip("底面の右前コーナーを表示するオブジェクトです。")]
        [SerializeField] private GameObject BottomCornerRightFront;

        [Tooltip("底面の左前コーナーを表示するオブジェクトです。")]
        [SerializeField] private GameObject BottomCornerLeftFront;

        [Tooltip("底面の右後コーナーを表示するオブジェクトです。")]
        [SerializeField] private GameObject BottomCornerRightBack;

        [Tooltip("底面の左後コーナーを表示するオブジェクトです。")]
        [SerializeField] private GameObject BottomCornerLeftBack;

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
            public float lastWorldYOffset;
            public bool isInitialized;
        }

        private TrackingState _track;

        #endregion

        #region 公開API

        #endregion

        #region 公開API

        /// <summary>
        /// 外部（Editorなど）から明示的にプレビュー更新したいときに呼びます。
        /// </summary>
        public void RefreshNow()
        {
            UpdatePreview();
        }

        #endregion

        #region Unityイベント

        /// <summary>
        /// コンポーネントが有効化された際に呼ばれます。
        /// </summary>
        private void OnEnable()
        {
            InitializeTracking(ref _track, ReferenceBox);
            _track.lastWorldYOffset = WorldYOffset;
            UpdatePreview();
        }

        /// <summary>
        /// 毎フレーム更新後に呼ばれます。変更検知してプレビューを更新します。
        /// </summary>
        private void LateUpdate()
        {
            if (ReferenceBox == null) return;

            if (!_track.isInitialized)
            {
                InitializeTracking(ref _track, ReferenceBox);
                _track.lastWorldYOffset = WorldYOffset;
                UpdatePreview();
                return;
            }

            if (HasBoxChanged(ref _track, ReferenceBox, WorldYOffset))
            {
                UpdatePreview();
            }
        }

        #endregion

        #region プレビュー更新

        /// <summary>
        /// プレビューを更新します。
        /// </summary>
        private void UpdatePreview()
        {
            UpdateBottomPreview(
                ReferenceBox,
                WorldYOffset,
                BottomFace,
                BottomCenter,
                BottomEdgeRight,
                BottomEdgeLeft,
                BottomEdgeFront,
                BottomEdgeBack,
                BottomCornerRightFront,
                BottomCornerLeftFront,
                BottomCornerRightBack,
                BottomCornerLeftBack
            );
        }

        /// <summary>
        /// BoxColliderの底面プレビューを更新します。
        /// </summary>
        /// <param name="box">対象のBoxCollider</param>
        /// <param name="worldYOffset">ワールドY軸方向のオフセット</param>
        /// <param name="bottomFace">底面全体のプレーンオブジェクト</param>
        /// <param name="bottomCenter">底面中心点のオブジェクト</param>
        /// <param name="bottomEdgeRight">右エッジのオブジェクト</param>
        /// <param name="bottomEdgeLeft">左エッジのオブジェクト</param>
        /// <param name="bottomEdgeFront">前エッジのオブジェクト</param>
        /// <param name="bottomEdgeBack">後エッジのオブジェクト</param>
        /// <param name="bottomCornerRightFront">右前コーナーのオブジェクト</param>
        /// <param name="bottomCornerLeftFront">左前コーナーのオブジェクト</param>
        /// <param name="bottomCornerRightBack">右後コーナーのオブジェクト</param>
        /// <param name="bottomCornerLeftBack">左後コーナーのオブジェクト</param>
        private static void UpdateBottomPreview(
            BoxCollider box,
            float worldYOffset,
            GameObject bottomFace,
            GameObject bottomCenter,
            GameObject bottomEdgeRight,
            GameObject bottomEdgeLeft,
            GameObject bottomEdgeFront,
            GameObject bottomEdgeBack,
            GameObject bottomCornerRightFront,
            GameObject bottomCornerLeftFront,
            GameObject bottomCornerRightBack,
            GameObject bottomCornerLeftBack)
        {
            if (box == null) return;

            var boxTransform = box.transform;
            if (boxTransform == null) return;

            Vector3 centerWorld = boxTransform.TransformPoint(box.center);
            Quaternion boxRotation = boxTransform.rotation;

            Vector3 sizeWorld = Vector3.Scale(box.size, boxTransform.lossyScale);
            float halfX = Mathf.Abs(sizeWorld.x) * 0.5f;
            float halfY = Mathf.Abs(sizeWorld.y) * 0.5f;
            float halfZ = Mathf.Abs(sizeWorld.z) * 0.5f;

            Vector3 right = boxTransform.right;
            Vector3 up = boxTransform.up;
            Vector3 forward = boxTransform.forward;

            // 底面中心（BoxCollider.center を含む）
            Vector3 bottomCenterWorld = centerWorld - up * halfY;

            // Z-fight回避：ワールドY+へ持ち上げ
            if (Mathf.Abs(worldYOffset) > 0.0000001f)
            {
                bottomCenterWorld += Vector3.up * worldYOffset;
            }

            // 底面プレーン（XZ面）
            ApplyFillPlane(bottomFace, bottomCenterWorld, boxRotation, sizeWorld.x, sizeWorld.z);

            // 底面中心点
            ApplyPoint(bottomCenter, bottomCenterWorld, boxRotation);

            // エッジ（底面）
            // Front/Back は幅方向（ローカルX）に伸びる
            ApplyEdgeLine(bottomEdgeFront, bottomCenterWorld + forward * halfZ, boxRotation, sizeWorld.x);
            ApplyEdgeLine(bottomEdgeBack, bottomCenterWorld - forward * halfZ, boxRotation, sizeWorld.x);

            // Left/Right は奥行き方向（ローカルZ）に伸びるので Y+90 回転
            Quaternion lineRotationZ = boxRotation * Quaternion.Euler(0f, 90f, 0f);
            ApplyEdgeLine(bottomEdgeLeft, bottomCenterWorld - right * halfX, lineRotationZ, sizeWorld.z);
            ApplyEdgeLine(bottomEdgeRight, bottomCenterWorld + right * halfX, lineRotationZ, sizeWorld.z);

            // 角（底面）
            ApplyPoint(bottomCornerRightFront, bottomCenterWorld + right * halfX + forward * halfZ, boxRotation);
            ApplyPoint(bottomCornerLeftFront, bottomCenterWorld - right * halfX + forward * halfZ, boxRotation);
            ApplyPoint(bottomCornerRightBack, bottomCenterWorld + right * halfX - forward * halfZ, boxRotation);
            ApplyPoint(bottomCornerLeftBack, bottomCenterWorld - right * halfX - forward * halfZ, boxRotation);
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
            state.lastWorldYOffset = 0f;
            state.isInitialized = true;
        }

        /// <summary>
        /// BoxColliderに変更があったかを判定します。
        /// </summary>
        /// <param name="state">トラッキング状態</param>
        /// <param name="box">対象のBoxCollider</param>
        /// <param name="worldYOffset">ワールドY軸方向のオフセット</param>
        /// <returns>変更があった場合true</returns>
        private static bool HasBoxChanged(ref TrackingState state, BoxCollider box, float worldYOffset)
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

            if (Mathf.Abs(worldYOffset - state.lastWorldYOffset) > 0.0001f)
            {
                state.lastWorldYOffset = worldYOffset;
                changed = true;
            }

            return changed;
        }

        #endregion

        #region オブジェクト適用ヘルパー

        /// <summary>
        /// プレーンオブジェクトに位置、回転、スケールを適用します。
        /// </summary>
        /// <param name="fillPlane">対象のプレーンオブジェクト</param>
        /// <param name="positionWorld">ワールド座標での位置</param>
        /// <param name="rotationWorld">ワールド座標での回転</param>
        /// <param name="widthWorld">ワールド座標での幅</param>
        /// <param name="depthWorld">ワールド座標での奥行き</param>
        private static void ApplyFillPlane(GameObject fillPlane, Vector3 positionWorld, Quaternion rotationWorld, float widthWorld, float depthWorld)
        {
            if (fillPlane == null) return;

            var t = fillPlane.transform;
            t.SetPositionAndRotation(positionWorld, rotationWorld);

            if (!TryGetPlaneLocalSize(fillPlane, out Vector2 localSize))
            {
                // メッシュサイズが取れない場合は「1mプレーン」前提で合わせる
                Vector3 fallback = t.localScale;
                fallback.x = Mathf.Max(0f, Mathf.Abs(widthWorld));
                fallback.z = Mathf.Max(0f, Mathf.Abs(depthWorld));
                t.localScale = fallback;
                return;
            }

            // localSize が (幅, 奥行き) を表す前提でスケールを合わせる
            float sx = localSize.x > 0.00001f ? Mathf.Abs(widthWorld) / localSize.x : 1f;
            float sz = localSize.y > 0.00001f ? Mathf.Abs(depthWorld) / localSize.y : 1f;

            Vector3 newScale = t.localScale;
            newScale.x = sx;
            newScale.z = sz;
            t.localScale = newScale;
        }

        /// <summary>
        /// プレーンオブジェクトのローカルサイズを取得します。
        /// </summary>
        /// <param name="target">対象のオブジェクト</param>
        /// <param name="size">取得したサイズ</param>
        /// <returns>サイズの取得に成功した場合true</returns>
        private static bool TryGetPlaneLocalSize(GameObject target, out Vector2 size)
        {
            size = Vector2.zero;

            var meshFilter = target.GetComponentInChildren<MeshFilter>();
            if (meshFilter == null) return false;

            var mesh = meshFilter.sharedMesh;
            if (mesh == null) return false;

            var b = mesh.bounds;
            Vector3 s = b.size;

            // Plane/Quadの想定：XZ面 or XY面
            float x = Mathf.Abs(s.x);
            float y = Mathf.Abs(s.y);
            float z = Mathf.Abs(s.z);

            const float eps = 0.00001f;

            // 通常のUnity Plane: (10, 0, 10)
            if (z > eps)
            {
                size = new Vector2(x, z);
                return true;
            }

            // Quad等: (1, 1, 0) のケース
            if (y > eps)
            {
                size = new Vector2(x, y);
                return true;
            }

            return false;
        }

        /// <summary>
        /// ポイントオブジェクトに位置と回転を適用します。
        /// </summary>
        /// <param name="target">対象のオブジェクト</param>
        /// <param name="positionWorld">ワールド座標での位置</param>
        /// <param name="rotationWorld">ワールド座標での回転</param>
        private static void ApplyPoint(GameObject target, Vector3 positionWorld, Quaternion rotationWorld)
        {
            if (target == null) return;
            target.transform.SetPositionAndRotation(positionWorld, rotationWorld);
        }

        /// <summary>
        /// エッジラインオブジェクトに位置、回転、長さを適用します。
        /// </summary>
        /// <param name="target">対象のオブジェクト</param>
        /// <param name="positionWorld">ワールド座標での位置</param>
        /// <param name="rotationWorld">ワールド座標での回転</param>
        /// <param name="lengthWorld">ワールド座標での長さ</param>
        private static void ApplyEdgeLine(GameObject target, Vector3 positionWorld, Quaternion rotationWorld, float lengthWorld)
        {
            if (target == null) return;

            var t = target.transform;
            t.SetPositionAndRotation(positionWorld, rotationWorld);

            // 「1mスケールのライン」前提で、ローカルXを長さに合わせる
            Vector3 localScale = t.localScale;
            localScale.x = Mathf.Max(0f, Mathf.Abs(lengthWorld));
            t.localScale = localScale;
        }

        #endregion

        #region エディタサポート

        /// <summary>
        /// エディタでの値の変更時に呼ばれる検証処理です。
        /// </summary>
        private void OnValidate()
        {
            InitializeTracking(ref _track, ReferenceBox);
            _track.lastWorldYOffset = WorldYOffset;
            UpdatePreview();
        }

        #endregion
    }
}
