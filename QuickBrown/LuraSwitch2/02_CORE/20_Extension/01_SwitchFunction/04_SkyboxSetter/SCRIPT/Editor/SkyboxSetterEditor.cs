#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(SkyboxSetter))]
public class SkyboxSetterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty fogModeProperty = serializedObject.FindProperty("customFogMode");
        bool isLinearFogMode = fogModeProperty != null && fogModeProperty.intValue == (int)FogMode.Linear;

        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            bool isFogStartDistance = iterator.propertyPath == "customFogStartDistance";
            bool isFogEndDistance = iterator.propertyPath == "customFogEndDistance";
            bool isFogDensity = iterator.propertyPath == "customFogDensity";

            if (isFogDensity && isLinearFogMode)
            {
                enterChildren = false;
                continue;
            }

            if ((isFogStartDistance || isFogEndDistance) && !isLinearFogMode)
            {
                enterChildren = false;
                continue;
            }

            bool isScript = iterator.propertyPath == "m_Script";
            bool isFogMode = iterator.propertyPath == "customFogMode";

            using (new EditorGUI.DisabledScope(isScript || isFogMode))
            {
                EditorGUILayout.PropertyField(iterator, true);
            }

            if (isFogMode)
            {
                EditorGUILayout.HelpBox("JP:Fog Mode はシーンの RenderSettings.fogMode を自動参照します（VRChatでは途中変更が不可能です）\nEN:Fog Mode is automatically referenced from the scene's RenderSettings.fogMode (cannot be changed during runtime in VRChat).", MessageType.Info);
            }

            enterChildren = false;
        }

        serializedObject.ApplyModifiedProperties();
    }
}

[InitializeOnLoad]
public static class SkyboxSetterPlayModeHandler
{
    static SkyboxSetterPlayModeHandler()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // プレイモード開始前に全てのプレビューをDefaultに戻す
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            SkyboxSetter[] skyboxSetters = Object.FindObjectsOfType<SkyboxSetter>(true);
            foreach (var setter in skyboxSetters)
            {
                if (setter != null)
                {
                    setter.ResetPreview();
                }
            }
        }
    }
}

[InitializeOnLoad]
public static class SkyboxSetterFogModeAutoSyncHandler
{
    private static bool _initialized;
    private static FogMode _lastFogMode;

    static SkyboxSetterFogModeAutoSyncHandler()
    {
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.hierarchyChanged += SyncCurrentFogModeToAllSetters;
    }

    private static void OnEditorUpdate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            _initialized = false;
            return;
        }

        FogMode currentFogMode = RenderSettings.fogMode;
        if (!_initialized)
        {
            _initialized = true;
            _lastFogMode = currentFogMode;
            SyncFogModeToAllSetters(currentFogMode);
            return;
        }

        if (_lastFogMode != currentFogMode)
        {
            _lastFogMode = currentFogMode;
            SyncFogModeToAllSetters(currentFogMode);
        }
    }

    private static void SyncCurrentFogModeToAllSetters()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        FogMode currentFogMode = RenderSettings.fogMode;
        _lastFogMode = currentFogMode;
        _initialized = true;
        SyncFogModeToAllSetters(currentFogMode);
    }

    private static void SyncFogModeToAllSetters(FogMode fogMode)
    {
        SkyboxSetter[] skyboxSetters = Object.FindObjectsOfType<SkyboxSetter>(true);
        for (int index = 0; index < skyboxSetters.Length; index++)
        {
            SkyboxSetter setter = skyboxSetters[index];
            if (setter == null)
            {
                continue;
            }

            SerializedObject serializedObject = new SerializedObject(setter);
            SerializedProperty fogModeProperty = serializedObject.FindProperty("customFogMode");
            if (fogModeProperty == null)
            {
                continue;
            }

            int targetFogModeValue = (int)fogMode;
            if (fogModeProperty.intValue == targetFogModeValue)
            {
                continue;
            }

            fogModeProperty.intValue = targetFogModeValue;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(setter);
        }
    }
}
#endif
