using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using QuickBrown.LuraSwitch;

[CustomEditor(typeof(MirrorController))]
public class MirrorControllerEditor : Editor
{
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 lastLocalScale;
    private bool isInitialized = false;

    private Vector3 lastSetupPosition;
    private Quaternion lastSetupRotation;
    private Vector3 lastSetupLocalScale;
    private bool isSetupInitialized = false;

    private static bool s_isGlobalHooked;
    private static System.Reflection.MethodInfo s_updateTriggerMethod;

    static MirrorControllerEditor()
    {
        EnsureGlobalHooks();
    }

    [InitializeOnLoadMethod]
    private static void EnsureGlobalHooks()
    {
        if (s_isGlobalHooked) return;
        s_isGlobalHooked = true;

        // Scene上でのTransform操作（Undoを伴う変更）を、MirrorControllerが未選択でも拾う
        Undo.postprocessModifications -= OnPostprocessModifications;
        Undo.postprocessModifications += OnPostprocessModifications;
    }

    private void OnEnable()
    {
        InitializeTransformTracking();
    }

    public override void OnInspectorGUI()
    {
        // デフォルトのInspector描画
        DrawDefaultInspector();

        // Transform変更の監視
        CheckTransformChanges();
    }

    private void OnSceneGUI()
    {
        // シーンビューでの操作も監視
        CheckTransformChanges();
    }

    /// <summary>
    /// Transform監視の初期化
    /// </summary>
    private void InitializeTransformTracking()
    {
        MirrorController controller = (MirrorController)target;
        if (controller != null)
        {
            lastPosition = controller.transform.position;
            lastRotation = controller.transform.rotation;
            lastLocalScale = controller.transform.localScale;
            isInitialized = true;

            InitializeSetupMirrorTracking(controller);
        }
    }

    private void InitializeSetupMirrorTracking(MirrorController controller)
    {
        var setupTransform = GetSetupMirrorTransform(controller);
        if (setupTransform == null)
        {
            isSetupInitialized = false;
            return;
        }

        lastSetupPosition = setupTransform.position;
        lastSetupRotation = setupTransform.rotation;
        lastSetupLocalScale = setupTransform.localScale;
        isSetupInitialized = true;
    }

    /// <summary>
    /// Transformの変更をチェックし、変更があった場合はMirrorTriggerを更新
    /// </summary>
    private void CheckTransformChanges()
    {
        MirrorController controller = (MirrorController)target;
        if (controller == null || !isInitialized)
        {
            InitializeTransformTracking();
            return;
        }

        // オブジェクトが破棄されている場合は処理をスキップ
        if (controller == null || controller.Equals(null))
            return;

        bool hasChanged = false;
        Transform currentTransform = controller.transform;

        // Transformが無効な場合は処理をスキップ
        if (currentTransform == null)
            return;

        try
        {
            // Position変更チェック
            if (lastPosition != currentTransform.position)
            {
                lastPosition = currentTransform.position;
                hasChanged = true;
            }

            // Rotation変更チェック
            if (lastRotation != currentTransform.rotation)
            {
                lastRotation = currentTransform.rotation;
                hasChanged = true;
            }

            // LocalScale変更チェック
            if (lastLocalScale != currentTransform.localScale)
            {
                lastLocalScale = currentTransform.localScale;
                hasChanged = true;
            }

            // SetupMirrorのTransform変更も監視
            var setupTransform = GetSetupMirrorTransform(controller);
            if (setupTransform != null)
            {
                if (!isSetupInitialized)
                {
                    InitializeSetupMirrorTracking(controller);
                }
                else
                {
                    if (Vector3.Distance(setupTransform.position, lastSetupPosition) > 0.001f)
                    {
                        lastSetupPosition = setupTransform.position;
                        hasChanged = true;
                    }

                    if (Quaternion.Angle(setupTransform.rotation, lastSetupRotation) > 0.001f)
                    {
                        lastSetupRotation = setupTransform.rotation;
                        hasChanged = true;
                    }

                    if (Vector3.Distance(setupTransform.localScale, lastSetupLocalScale) > 0.001f)
                    {
                        lastSetupLocalScale = setupTransform.localScale;
                        hasChanged = true;
                    }
                }
            }

            // 変更があった場合はMirrorTriggerを更新
            if (hasChanged)
            {
                // リフレクションを使用してprivateメソッドを呼び出し
                System.Reflection.MethodInfo updateTriggerMethod = typeof(MirrorController)
                    .GetMethod("UpdateMirrorTrigger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (updateTriggerMethod != null && controller != null && !controller.Equals(null))
                {
                    updateTriggerMethod.Invoke(controller, null);
                }

                // 現在アクティブなミラーオブジェクトにもTransformを同期
                SyncCurrentMirrorTransform(controller);

                // AreaPreviewも更新（SetupMirrorの変形に追従させる）
                RefreshAreaPreviews(controller);

                // シーンを変更済みとしてマーク
                if (!Application.isPlaying && controller != null && !controller.Equals(null))
                {
                    EditorUtility.SetDirty(controller);
                }
            }
        }
        catch (System.Exception e)
        {
            // MissingReferenceExceptionは静かに処理
            if (!(e is UnityEngine.MissingReferenceException))
            {
                UnityEngine.Debug.LogException(e);
            }
        }
    }

    private static Transform GetSetupMirrorTransform(MirrorController controller)
    {
        if (controller == null || controller.Equals(null)) return null;

        try
        {
            var setupMirrorField = typeof(MirrorController).GetField(
                "SetupMirror",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );
            if (setupMirrorField == null) return null;

            var setupMirrorObject = setupMirrorField.GetValue(controller) as GameObject;
            if (setupMirrorObject == null) return null;

            return setupMirrorObject.transform;
        }
        catch
        {
            return null;
        }
    }

    private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
    {
        if (modifications == null || modifications.Length == 0) return modifications;
        if (Application.isPlaying) return modifications;

        // Transformの変更を拾う
        var changedTransforms = new HashSet<Transform>();
        for (int i = 0; i < modifications.Length; i++)
        {
            var target = modifications[i].currentValue.target;
            if (target is Transform t)
            {
                changedTransforms.Add(t);
            }
        }

        if (changedTransforms.Count == 0) return modifications;

        // 変更がSetupMirror配下に影響するMirrorControllerだけ更新
        RefreshControllersForTransformChanges(changedTransforms);

        return modifications;
    }

    private static void RefreshControllersForTransformChanges(HashSet<Transform> changedTransforms)
    {
        if (changedTransforms == null || changedTransforms.Count == 0) return;

        if (s_updateTriggerMethod == null)
        {
            s_updateTriggerMethod = typeof(MirrorController)
                .GetMethod("UpdateMirrorTrigger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        }

#if UNITY_2020_1_OR_NEWER
        var controllers = Object.FindObjectsOfType<MirrorController>(true);
#else
        var controllers = Object.FindObjectsOfType<MirrorController>();
#endif
        if (controllers == null || controllers.Length == 0) return;

        foreach (var controller in controllers)
        {
            if (controller == null || controller.Equals(null)) continue;

            var setupTransform = GetSetupMirrorTransform(controller);
            if (setupTransform == null) continue;

            bool affected = false;
            foreach (var changed in changedTransforms)
            {
                if (changed == null) continue;

                // SetupMirror自身、またはその子孫が動いたら影響あり
                if (changed == setupTransform || changed.IsChildOf(setupTransform))
                {
                    affected = true;
                    break;
                }

                // 逆に、SetupMirrorが変更Transform配下にいるケース（親を動かした等）
                if (setupTransform.IsChildOf(changed))
                {
                    affected = true;
                    break;
                }
            }

            if (!affected) continue;

            if (s_updateTriggerMethod != null)
            {
                try
                {
                    s_updateTriggerMethod.Invoke(controller, null);
                }
                catch
                {
                    // 失敗してもAreaPreview更新は試みる
                }
            }

            RefreshAreaPreviews(controller);
            EditorUtility.SetDirty(controller);
        }
    }

    /// <summary>
    /// MirrorController配下/SetupMirror配下に存在するAreaPreviewを更新する
    /// </summary>
    private static void RefreshAreaPreviews(MirrorController controller)
    {
        if (controller == null || controller.Equals(null)) return;

        // まずSetupMirror配下を優先して探す
        GameObject setupMirrorObject = null;
        try
        {
            var setupMirrorField = typeof(MirrorController).GetField(
                "SetupMirror",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );
            if (setupMirrorField != null)
            {
                setupMirrorObject = setupMirrorField.GetValue(controller) as GameObject;
            }
        }
        catch
        {
            // リフレクション失敗は無視
        }

        if (setupMirrorObject != null)
        {
            var previews = setupMirrorObject.GetComponentsInChildren<MirrorAreaPreview>(true);
            if (previews != null && previews.Length > 0)
            {
                foreach (var preview in previews)
                {
                    if (preview == null) continue;
                    preview.RefreshNow();
                    EditorUtility.SetDirty(preview);
                }
                return;
            }
        }

        // フォールバック：コントローラ配下
        {
            var previews = controller.GetComponentsInChildren<MirrorAreaPreview>(true);
            if (previews == null) return;
            foreach (var preview in previews)
            {
                if (preview == null) continue;
                preview.RefreshNow();
                EditorUtility.SetDirty(preview);
            }
        }
    }

    /// <summary>
    /// 現在アクティブなミラーオブジェクトのTransformをSetupMirrorと同期する
    /// </summary>
    /// <param name="controller">MirrorControllerのインスタンス</param>
    private void SyncCurrentMirrorTransform(MirrorController controller)
    {
        // リフレクションを使用してprivateフィールドとメソッドにアクセス
        System.Reflection.FieldInfo currentMirrorTypeField = typeof(MirrorController)
            .GetField("previewMirrorType", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        System.Reflection.MethodInfo findMirrorObjectMethod = typeof(MirrorController)
            .GetMethod("FindMirrorObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        System.Reflection.MethodInfo syncTransformMethod = typeof(MirrorController)
            .GetMethod("SyncTransformFromSetupMirror", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (currentMirrorTypeField != null && findMirrorObjectMethod != null && syncTransformMethod != null)
        {
            // 現在のミラータイプを取得
            var currentMirrorType = currentMirrorTypeField.GetValue(controller);

            // SetupMirror以外の場合のみ同期処理を実行
            if (currentMirrorType != null && !currentMirrorType.ToString().Equals("SetupMirror"))
            {
                // 現在アクティブなミラーオブジェクトを取得
                GameObject currentMirrorObject = (GameObject)findMirrorObjectMethod.Invoke(controller, new object[] { currentMirrorType });

                // ミラーオブジェクトが存在し、アクティブな場合のみTransformを同期
                if (currentMirrorObject != null && currentMirrorObject.activeInHierarchy)
                {
                    syncTransformMethod.Invoke(controller, new object[] { currentMirrorObject });
                }
            }
        }
    }
}