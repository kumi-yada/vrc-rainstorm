using UnityEngine;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// HeightOffsetterの有効範囲（ReferenceBox）を可視化するプレビュー機能です（エディタでも動作）。
    /// 変更検知により必要なときだけ更新する設計です。
    /// </summary>
    [ExecuteAlways]
    public class HeightOffsetterPreview : MonoBehaviour
    {
        #region インスペクターフィールド

        [Tooltip("プレビュー対象のBoxColliderです。")]
        [SerializeField] private BoxCollider ReferenceBox;

        [Header("Vertical Face")]
        [Tooltip("垂直面全体を表示するオブジェクトです。")]
        [SerializeField] private GameObject CenterFace;

        [Tooltip("垂直面の上端中心を表示するオブジェクトです。")]
        [SerializeField] private GameObject CenterFaceTopCenter;

        [Tooltip("垂直面の下端中心を表示するオブジェクトです。")]
        [SerializeField] private GameObject CenterFaceBottomCenter;

        [Header("Vertical Face Points")]
        [Tooltip("垂直面の左上ポイントを表示するオブジェクトです。")]
        [SerializeField] private GameObject CenterTopLeftPoint;

        [Tooltip("垂直面の左下ポイントを表示するオブジェクトです。")]
        [SerializeField] private GameObject CenterBottomLeftPoint;

        [Header("Vertical Face Lines")]
        [Tooltip("垂直面の上端ラインを表示するオブジェクトです。")]
        [SerializeField] private GameObject CenterTopLine;

        [Tooltip("垂直面の下端ラインを表示するオブジェクトです。")]
        [SerializeField] private GameObject CenterBottomLine;

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
            public bool isInitialized;
        }

        private TrackingState _track;

        #endregion

        #region 公開API

        /// <summary>
        /// 外部（Editorなど）から明示的にプレビュー更新したいときに呼ぶ
        /// </summary>
        public void RefreshNow()
        {
            UpdatePreview();
        }

        private void OnEnable()
        {
            InitializeTracking(ref _track, ReferenceBox);
            UpdatePreview();
        }

        private void LateUpdate()
        {
            // EditModeでも毎フレーム回るので、変更があるときだけ更新する
            if (ReferenceBox == null) return;

            if (!_track.isInitialized)
            {
                InitializeTracking(ref _track, ReferenceBox);
                UpdatePreview();
                return;
            }

            if (HasBoxChanged(ref _track, ReferenceBox))
            {
                UpdatePreview();
            }
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
            state.isInitialized = true;
        }

        /// <summary>
        /// BoxColliderに変更があったかを判定します。
        /// </summary>
        /// <param name="state">トラッキング状態</param>
        /// <param name="box">対象のBoxCollider</param>
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
        /// プレビューを更新します。
        /// </summary>
        private void UpdatePreview()
        {
            UpdateBoxPreview(
                ReferenceBox,
                CenterFace,
                CenterFaceTopCenter,
                CenterFaceBottomCenter,
                CenterTopLeftPoint,
                CenterBottomLeftPoint,
                CenterTopLine,
                CenterBottomLine
            );
        }

        /// <summary>
        /// BoxColliderの垂直面プレビューを更新します。
        /// </summary>
        private static void UpdateBoxPreview(
        BoxCollider box,
        GameObject centerFace,
        GameObject centerFaceTopCenter,
        GameObject centerFaceBottomCenter,
        GameObject centerTopLeftPoint,
        GameObject centerBottomLeftPoint,
        GameObject centerTopLine,
        GameObject centerBottomLine)
        {
            if (box == null) return;

            var boxTransform = box.transform;
            if (boxTransform == null) return;

            Vector3 centerWorld = boxTransform.TransformPoint(box.center);
            Quaternion boxRotation = boxTransform.rotation;

            Vector3 sizeWorld = Vector3.Scale(box.size, boxTransform.lossyScale);
            float halfX = Mathf.Abs(sizeWorld.x) * 0.5f;
            float halfY = Mathf.Abs(sizeWorld.y) * 0.5f;
            Vector3 up = boxTransform.up;
            Vector3 right = boxTransform.right;

            // 垂直面：天面と底面をつなぐ「壁」（サイズは X(幅) × Y(高さ)）
            ApplyVerticalFace(centerFace, centerWorld, boxRotation, Mathf.Abs(sizeWorld.x), Mathf.Abs(sizeWorld.y));

            // 垂直面の上端/下端の中心点
            Vector3 topEdgeCenterWorld = centerWorld + up * halfY;
            Vector3 bottomEdgeCenterWorld = centerWorld - up * halfY;
            ApplyPoint(centerFaceTopCenter, topEdgeCenterWorld, boxRotation);
            ApplyPoint(centerFaceBottomCenter, bottomEdgeCenterWorld, boxRotation);

            // 垂直面の上端/下端のライン（幅方向）
            ApplyLineX(centerTopLine, topEdgeCenterWorld, boxRotation, Mathf.Abs(sizeWorld.x));
            ApplyLineX(centerBottomLine, bottomEdgeCenterWorld, boxRotation, Mathf.Abs(sizeWorld.x));

            // 垂直面の左上/左下（ローカル -right 側）
            Vector3 leftOffset = -right * halfX;
            ApplyPoint(centerTopLeftPoint, topEdgeCenterWorld + leftOffset, boxRotation);
            ApplyPoint(centerBottomLeftPoint, bottomEdgeCenterWorld + leftOffset, boxRotation);
        }

        #endregion

        #region オブジェクト適用ヘルパー

        /// <summary>
        /// 垂直面（壁）オブジェクトに位置、回転、スケールを適用します。
        /// </summary>
        /// <param name="plane">対象の面オブジェクト</param>
        /// <param name="positionWorld">ワールド座標での位置</param>
        /// <param name="boxRotation">BoxColliderの回転</param>
        /// <param name="widthWorld">ワールド座標での幅</param>
        /// <param name="heightWorld">ワールド座標での高さ</param>
        private static void ApplyVerticalFace(GameObject plane, Vector3 positionWorld, Quaternion boxRotation, float widthWorld, float heightWorld)
        {
            if (plane == null) return;

            bool gotSize = TryGetPlaneLocalSize(plane, out Vector2 localSize, out bool isXZPlane);
            Quaternion rotationWorld = isXZPlane
                ? boxRotation * Quaternion.Euler(90f, 0f, 0f) // Plane(XZ) を壁向きに
                : boxRotation; // Quad(XY) はそのまま壁

            var t = plane.transform;
            t.SetPositionAndRotation(positionWorld, rotationWorld);

            if (!gotSize)
            {
                // メッシュサイズが取れない場合は「1mプレーン」前提で合わせる
                Vector3 fallback = t.localScale;
                fallback.x = Mathf.Max(0f, Mathf.Abs(widthWorld));
                if (isXZPlane)
                {
                    fallback.z = Mathf.Max(0f, Mathf.Abs(heightWorld));
                }
                else
                {
                    fallback.y = Mathf.Max(0f, Mathf.Abs(heightWorld));
                }
                t.localScale = fallback;
                return;
            }

            // localSize が (ローカル幅, ローカル高さ/奥行き) を表す前提でスケールを合わせる
            float sx = localSize.x > 0.00001f ? Mathf.Abs(widthWorld) / localSize.x : 1f;
            float sz = localSize.y > 0.00001f ? Mathf.Abs(heightWorld) / localSize.y : 1f;

            Vector3 newScale = t.localScale;
            newScale.x = sx;
            if (isXZPlane)
            {
                newScale.z = sz;
            }
            else
            {
                newScale.y = sz;
            }
            t.localScale = newScale;
        }

        /// <summary>
        /// プレーンオブジェクトのローカルサイズと向きを取得します。
        /// </summary>
        /// <param name="target">対象のオブジェクト</param>
        /// <param name="size">取得したサイズ</param>
        /// <param name="isXZPlane">XZ面かどうか（falseの場合はXY面）</param>
        /// <returns>サイズの取得に成功した場合true</returns>
        private static bool TryGetPlaneLocalSize(GameObject target, out Vector2 size, out bool isXZPlane)
        {
            size = Vector2.zero;
            isXZPlane = true;

            var meshFilter = target.GetComponentInChildren<MeshFilter>();
            if (meshFilter == null) return false;

            var mesh = meshFilter.sharedMesh;
            if (mesh == null) return false;

            var b = mesh.bounds;
            Vector3 s = b.size;

            float x = Mathf.Abs(s.x);
            float y = Mathf.Abs(s.y);
            float z = Mathf.Abs(s.z);

            const float eps = 0.00001f;

            // Unity Plane: (10, 0, 10) など（XZ面）
            if (z > eps)
            {
                size = new Vector2(x, z);
                isXZPlane = true;
                return true;
            }

            // Quad等: (1, 1, 0) のケース（XY面）
            if (y > eps)
            {
                size = new Vector2(x, y);
                isXZPlane = false;
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
        /// ラインオブジェクトに位置、回転、長さを適用します。
        /// </summary>
        /// <param name="target">対象のオブジェクト</param>
        /// <param name="positionWorld">ワールド座標での位置</param>
        /// <param name="rotationWorld">ワールド座標での回転</param>
        /// <param name="lengthWorld">ワールド座標での長さ</param>
        private static void ApplyLineX(GameObject target, Vector3 positionWorld, Quaternion rotationWorld, float lengthWorld)
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
            UpdatePreview();
        }

        #endregion
    }
}
