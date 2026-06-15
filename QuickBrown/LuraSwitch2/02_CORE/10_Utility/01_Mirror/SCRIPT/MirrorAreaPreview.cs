using UnityEngine;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// エディタ上でミラートリガーエリアを視覚化するプレビュー表示クラス。
    /// BoxColliderのサイズ/回転/中心に追従してガイドオブジェクトを自動更新します（エディタでも動作）。
    /// </summary>
    [ExecuteAlways]
    public class MirrorAreaPreview : MonoBehaviour
    {
        #region インスペクターフィールド

        [Header("Box Colliders")]
        [Tooltip("プレビュー対象のBoxCollider 1（開始エリア）")]
        [SerializeField] private BoxCollider Box_1;

        [Tooltip("プレビュー対象のBoxCollider 2（完全反映エリア）")]
        [SerializeField] private BoxCollider Box_2;

        [Header("Guide Height")]
        [Tooltip("ガイド表示の高さ（0=底面、0.5=中央、1=天面）")]
        [SerializeField, Range(0f, 1f)]
        private float guideHeight = 0.25f;

        [Header("Box 1 Preview Objects")]
        [SerializeField] private GameObject Box1_BoxCenter;
        [SerializeField] private GameObject Box1_FillPlane;
        [SerializeField] private GameObject Box1_EdgeLine_Back;
        [SerializeField] private GameObject Box1_EdgeLine_Back_half;
        [SerializeField] private GameObject Box1_EdgeLine_Left;
        [SerializeField] private GameObject Box1_EdgeLine_Left_half;
        [SerializeField] private GameObject Box1_EdgeLine_Right;
        [SerializeField] private GameObject Box1_EdgeLine_Right_half;

        [SerializeField] private GameObject Box1_Corner_BackLeft;
        [SerializeField] private GameObject Box1_Corner_BackRight;
        [SerializeField] private GameObject Box1_Corner_FrontLeft;
        [SerializeField] private GameObject Box1_Corner_FrontRight;

        [Header("Box 2 Preview Objects")]
        [SerializeField] private GameObject Box2_BoxCenter;
        [SerializeField] private GameObject Box2_FillPlane;
        [SerializeField] private GameObject Box2_EdgeLine_Back;
        [SerializeField] private GameObject Box2_EdgeLine_Back_half;
        [SerializeField] private GameObject Box2_EdgeLine_Left;
        [SerializeField] private GameObject Box2_EdgeLine_Left_half;
        [SerializeField] private GameObject Box2_EdgeLine_Right;
        [SerializeField] private GameObject Box2_EdgeLine_Right_half;

        [SerializeField] private GameObject Box2_Corner_BackLeft;
        [SerializeField] private GameObject Box2_Corner_BackRight;
        [SerializeField] private GameObject Box2_Corner_FrontLeft;
        [SerializeField] private GameObject Box2_Corner_FrontRight;

        [SerializeField] private GameObject FrontCenter;

        #endregion

        #region トラッキング状態

        /// <summary>
        /// BoxColliderの変更を検出するための追跡状態
        /// </summary>
        private struct TrackingState
        {
            public Vector3 lastPosition;
            public Quaternion lastRotation;
            public Vector3 lastLossyScale;
            public Vector3 lastColliderCenter;
            public Vector3 lastColliderSize;
            public bool isInitialized;
        }

        private TrackingState _track1;
        private TrackingState _track2;

        #endregion

        #region 公開API

        /// <summary>
        /// 外部（Editorなど）から明示的にプレビュー更新したいときに呼びます。
        /// </summary>
        public void RefreshNow()
        {
            UpdateBox1Preview();
            UpdateBox2Preview();
        }

        #endregion

        #region Unityイベント

        /// <summary>
        /// コンポーネント有効化時の初期化処理
        /// </summary>
        private void OnEnable()
        {
            InitializeTracking(ref _track1, Box_1);
            InitializeTracking(ref _track2, Box_2);
            UpdateBox1Preview();
            UpdateBox2Preview();
        }

        /// <summary>
        /// インスペクター値変更時の処理
        /// </summary>
        private void OnValidate()
        {
            InitializeTracking(ref _track1, Box_1);
            InitializeTracking(ref _track2, Box_2);
            UpdateBox1Preview();
            UpdateBox2Preview();
        }

        /// <summary>
        /// 毎フレーム実行され、BoxColliderの変更を検出してプレビューを更新します。
        /// EditModeでも動作します。
        /// </summary>
        private void LateUpdate()
        {
            bool updated = false;

            if (Box_1 != null)
            {
                if (!_track1.isInitialized)
                {
                    InitializeTracking(ref _track1, Box_1);
                    UpdateBox1Preview();
                    updated = true;
                }
                else if (HasBoxChanged(ref _track1, Box_1))
                {
                    UpdateBox1Preview();
                    updated = true;
                }
            }

            if (Box_2 != null)
            {
                if (!_track2.isInitialized)
                {
                    InitializeTracking(ref _track2, Box_2);
                    UpdateBox2Preview();
                    updated = true;
                }
                else if (HasBoxChanged(ref _track2, Box_2))
                {
                    UpdateBox2Preview();
                    updated = true;
                }
            }

            _ = updated;
        }

        #endregion

        #region トラッキング管理

        /// <summary>
        /// トラッキング状態を初期化します。
        /// </summary>
        /// <param name="state">初期化するトラッキング状態</param>
        /// <param name="box">追跡対象のBoxCollider</param>
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
            state.isInitialized = true;
        }

        /// <summary>
        /// BoxColliderに変更があったかを検出します。
        /// </summary>
        /// <param name="state">現在のトラッキング状態</param>
        /// <param name="box">チェック対象のBoxCollider</param>
        /// <returns>変更があった場合true</returns>
        private static bool HasBoxChanged(ref TrackingState state, BoxCollider box)
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

            return changed;
        }

        #endregion

        #region プレビュー更新

        /// <summary>
        /// Box1のプレビューを更新します。
        /// </summary>
        private void UpdateBox1Preview()
        {
            UpdateBoxPreview(
                Box_1,
                guideHeight,
                Box1_BoxCenter,
                Box1_FillPlane,
                Box1_EdgeLine_Back,
                Box1_EdgeLine_Back_half,
                Box1_EdgeLine_Left,
                Box1_EdgeLine_Left_half,
                Box1_EdgeLine_Right,
                Box1_EdgeLine_Right_half,
                Box1_Corner_BackLeft,
                Box1_Corner_BackRight,
                Box1_Corner_FrontLeft,
                Box1_Corner_FrontRight,
                null
            );
        }

        /// <summary>
        /// Box2のプレビューを更新します。
        /// </summary>
        private void UpdateBox2Preview()
        {
            UpdateBoxPreview(
                Box_2,
                guideHeight,
                Box2_BoxCenter,
                Box2_FillPlane,
                Box2_EdgeLine_Back,
                Box2_EdgeLine_Back_half,
                Box2_EdgeLine_Left,
                Box2_EdgeLine_Left_half,
                Box2_EdgeLine_Right,
                Box2_EdgeLine_Right_half,
                Box2_Corner_BackLeft,
                Box2_Corner_BackRight,
                Box2_Corner_FrontLeft,
                Box2_Corner_FrontRight,
                FrontCenter
            );
        }

        /// <summary>
        /// BoxColliderに基づいてプレビューオブジェクトの位置・スケールを更新します。
        /// </summary>
        private static void UpdateBoxPreview(
            BoxCollider box,
            float guideHeightFromBottom01,
            GameObject boxCenter,
            GameObject fillPlane,
            GameObject edgeBack,
            GameObject edgeBackHalf,
            GameObject edgeLeft,
            GameObject edgeLeftHalf,
            GameObject edgeRight,
            GameObject edgeRightHalf,
            GameObject cornerBackLeft,
            GameObject cornerBackRight,
            GameObject cornerFrontLeft,
            GameObject cornerFrontRight,
            GameObject frontCenter)
        {
            if (box == null) return;

            var boxTransform = box.transform;
            if (boxTransform == null) return;

            Vector3 centerWorld = boxTransform.TransformPoint(box.center);
            Quaternion boxRotation = boxTransform.rotation;

            Vector3 sizeWorld = Vector3.Scale(box.size, boxTransform.lossyScale);
            float halfX = Mathf.Abs(sizeWorld.x) * 0.5f;
            float halfZ = Mathf.Abs(sizeWorld.z) * 0.5f;

            Vector3 right = boxTransform.right;
            Vector3 up = boxTransform.up;
            Vector3 forward = boxTransform.forward;

            // 配置する高さ（0=底面, 0.5=中心, 1=天面）
            float height = Mathf.Abs(sizeWorld.y);
            float t01 = Mathf.Clamp01(guideHeightFromBottom01);
            float offsetFromCenter = (t01 - 0.5f) * height;
            Vector3 guideCenterWorld = centerWorld + up * offsetFromCenter;

            if (boxCenter != null)
            {
                boxCenter.transform.SetPositionAndRotation(guideCenterWorld, boxRotation);
            }

            ApplyFillPlane(fillPlane, guideCenterWorld, boxRotation, sizeWorld.x, sizeWorld.z);
            ApplyEdgeLine(edgeBack, guideCenterWorld - forward * halfZ, boxRotation, sizeWorld.x);

            Quaternion lineRotationZ = boxRotation * Quaternion.Euler(0f, 90f, 0f);
            ApplyEdgeLine(edgeLeft, guideCenterWorld - right * halfX, lineRotationZ, sizeWorld.z);
            ApplyEdgeLine(edgeRight, guideCenterWorld + right * halfX, lineRotationZ, sizeWorld.z);

            ApplyPointAtEdgeMidpoint(edgeBackHalf, edgeBack);
            ApplyPointAtEdgeMidpoint(edgeLeftHalf, edgeLeft);
            ApplyPointAtEdgeMidpoint(edgeRightHalf, edgeRight);

            // Corner（四隅）配置
            ApplyPoint(cornerBackLeft, guideCenterWorld - right * halfX - forward * halfZ, boxRotation);
            ApplyPoint(cornerBackRight, guideCenterWorld + right * halfX - forward * halfZ, boxRotation);
            ApplyPoint(cornerFrontLeft, guideCenterWorld - right * halfX + forward * halfZ, boxRotation);
            ApplyPoint(cornerFrontRight, guideCenterWorld + right * halfX + forward * halfZ, boxRotation);

            // FrontCenter（前面の中心）
            ApplyPoint(frontCenter, centerWorld + forward * halfZ, boxRotation);

            // FrontCenterのスケールは「Box2前面（XY面）」の短辺に合わせる
            if (frontCenter != null)
            {
                float faceShortEdge = Mathf.Min(Mathf.Abs(sizeWorld.x), Mathf.Abs(sizeWorld.y));
                float s = Mathf.Max(0f, faceShortEdge);
                frontCenter.transform.localScale = Vector3.one * s;
            }
        }

        #endregion

        #region オブジェクト適用ヘルパー

        /// <summary>
        /// 塗りつぶしプレーンオブジェクトの位置とスケールを適用します。
        /// </summary>
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
        /// プレーンメッシュのローカルサイズを取得します。
        /// </summary>
        /// <param name="target">対象のGameObject</param>
        /// <param name="size">取得したサイズ（幅、奥行き）</param>
        /// <returns>サイズを取得できた場合true</returns>
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
        /// エッジラインオブジェクトの位置、回転、スケールを適用します。
        /// </summary>
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

        /// <summary>
        /// エッジラインの中点にポイントオブジェクトを配置します。
        /// </summary>
        private static void ApplyPointAtEdgeMidpoint(GameObject point, GameObject edgeLine)
        {
            if (point == null || edgeLine == null) return;

            Vector3 midpointWorld = GetVisualMidpointWorld(edgeLine);
            point.transform.SetPositionAndRotation(midpointWorld, edgeLine.transform.rotation);
        }

        /// <summary>
        /// オブジェクトの視覚的な中点を取得します（Renderer.boundsを使用）。
        /// </summary>
        private static Vector3 GetVisualMidpointWorld(GameObject target)
        {
            if (target == null) return Vector3.zero;

            // Pivotが端にある場合でも「見た目の中心」を取れるよう Renderer.bounds.center を優先
            var renderer = target.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                return renderer.bounds.center;
            }

            return target.transform.position;
        }

        /// <summary>
        /// ポイントオブジェクトの位置と回転を適用します。
        /// </summary>
        private static void ApplyPoint(GameObject target, Vector3 positionWorld, Quaternion rotationWorld)
        {
            if (target == null) return;
            target.transform.SetPositionAndRotation(positionWorld, rotationWorld);
        }

        #endregion
    }
}
