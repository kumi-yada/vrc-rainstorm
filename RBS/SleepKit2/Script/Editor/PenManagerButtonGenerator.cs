#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RBS.SleepKit2.Script.Editor
{
    /// <summary>
    /// シーン保存時に自動で、シーン内の QvPen.UdonScript.QvPen_PenManager に対応するボタンを生成するクラスです。
    /// EditorWindow は使用せず、専用のマーカークラス (PenManagerButtonGeneratorSettings) から設定値を取得して処理を行います.
    ///
    /// マーカークラスの設定例：
    ///  - buttonContainer：シーン内に配置されたボタン配置用コンテナ
    ///  - teleportTarget：シーン内に配置されたテレポート先
    ///  - buttonPrefab：プロジェクト内の "Assets/Prefabs/PenButton.prefab" など
    ///  - gradientTextureResolution：グラデーションテクスチャの横解像度
    ///  - objectToDisable：QvPen が存在しない場合に無効化、存在する場合は有効化するオブジェクト
    /// </summary>
    [InitializeOnLoad]
    public static class PenManagerButtonGenerator
    {
        // マーカークラスから取得する設定値（保持用）
        private static Transform buttonContainer;
        private static GameObject buttonPrefab;
        private static Transform teleportTarget;
        private static int gradientTextureResolution;

        static PenManagerButtonGenerator()
        {
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        /// <summary>
        /// シーン保存時のイベントハンドラ。
        /// マーカークラスから設定値を取得し、QvPen の存在チェックを行った上で、
        /// QvPen が存在しない場合は objectToDisable を無効化、存在する場合は有効化し、ボタン生成処理を実施します。
        /// </summary>
        private static void OnSceneSaving(Scene scene, string path)
        {
            // マーカークラスから設定値を取得
            PenManagerButtonGeneratorSettings settings =
                GameObject.FindObjectOfType<PenManagerButtonGeneratorSettings>();
            if (settings == null)
            {
                Debug.LogWarning(
                    "[PenManagerButtonGenerator] マーカークラス PenManagerButtonGeneratorSettings がシーン内に見つからなかったため、ボタン生成処理をスルーします。"
                );
                return;
            }

            buttonContainer = settings.buttonContainer;
            buttonPrefab = settings.buttonPrefab;
            teleportTarget = settings.teleportTarget;
            gradientTextureResolution = settings.gradientTextureResolution;

            if (buttonContainer == null || buttonPrefab == null || teleportTarget == null)
            {
                Debug.LogWarning(
                    "[PenManagerButtonGenerator] マーカークラスの設定値が不足しているため、シーン保存時にボタン生成処理をスルーします。"
                );
                return;
            }

            // QvPen の存在チェック（QvPen.UdonScript.QvPen_PenManager 型を取得）
            Type penManagerType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                penManagerType = assembly.GetType("QvPen.UdonScript.QvPen_PenManager");
                if (penManagerType != null)
                    break;
            }

            // QvPen が存在しない場合、設定された objectToDisable を無効化して処理終了
            if (penManagerType == null)
            {
                Debug.LogWarning(
                    "[PenManagerButtonGenerator] QvPen.UdonScript.QvPen_PenManager が見つからなかったため、設定された objectToDisable を無効化します。"
                );
                if (settings.objectToDisable != null)
                {
                    settings.objectToDisable.SetActive(false);
                }
                return;
            }
            // QvPen が存在する場合は、一旦 objectToDisable を有効化する
            if (settings.objectToDisable != null)
            {
                settings.objectToDisable.SetActive(true);
            }

            // シーン内の QvPen_PenManager コンポーネントを全て取得
            UnityEngine.Object[] penManagers = GameObject.FindObjectsOfType(penManagerType);
            if (penManagers == null || penManagers.Length == 0)
            {
                Debug.Log(
                    "[PenManagerButtonGenerator] シーン内に QvPen_PenManager コンポーネントが見つかりませんでした。"
                );
                // シーン内に QvPen_PenManager が存在しない場合も objectToDisable を無効化する
                if (settings.objectToDisable != null)
                {
                    settings.objectToDisable.SetActive(false);
                }
                return;
            }

            // 生成対象のコンテナに対して Undo 記録
            Undo.RecordObject(buttonContainer, "Generate Pen Manager Buttons");

            // 既存のボタンが重複しないように、buttonContainer内の子オブジェクトを全て削除
            for (int i = buttonContainer.childCount - 1; i >= 0; i--)
            {
                GameObject child = buttonContainer.GetChild(i).gameObject;
                Undo.DestroyObjectImmediate(child);
            }

            // QvPen_PenManager 内の "colorGradient" フィールド（public）を取得
            FieldInfo gradientField = penManagerType.GetField(
                "colorGradient",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic
            );
            // QvPen_PenManager 内の "pen" フィールド（public）を取得
            FieldInfo penField = penManagerType.GetField(
                "pen",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic
            );

            int createdCount = 0;
            foreach (var managerObj in penManagers)
            {
                Component managerComponent = managerObj as Component;
                if (managerComponent == null)
                    continue;

                // ボタンプレハブを生成（シーン内オブジェクトとして生成）
                GameObject buttonInstance = (GameObject)
                    PrefabUtility.InstantiatePrefab(buttonPrefab, buttonContainer);
                if (buttonInstance == null)
                {
                    Debug.LogWarning(
                        "[PenManagerButtonGenerator] ボタンプレハブの生成に失敗しました。"
                    );
                    continue;
                }

                // 分かりやすいように、生成したボタン名にペンマネージャのオブジェクト名を付加
                buttonInstance.name = "PenButton_" + managerComponent.gameObject.name;

                // QvPen_PenManager の colorGradient フィールドからグラデーションテクスチャを生成
                if (gradientField != null)
                {
                    Gradient gradient = gradientField.GetValue(managerObj) as Gradient;
                    if (gradient != null)
                    {
                        Texture2D gradientTex = GenerateGradientTexture(
                            gradient,
                            gradientTextureResolution
                        );
                        // 生成したテクスチャから Sprite を作成
                        Sprite gradientSprite = Sprite.Create(
                            gradientTex,
                            new Rect(0, 0, gradientTex.width, gradientTex.height),
                            new Vector2(0.5f, 0.5f)
                        );
                        // ボタンの Image コンポーネントにセット
                        Image img = buttonInstance.GetComponent<Image>();
                        if (img != null)
                        {
                            img.sprite = gradientSprite;
                        }
                    }
                }

                // ボタンに付いている PenManagerButton (UdonSharp) スクリプトに、ペンとテレポート先を設定
                RBS.SleepKit2.Script.PenManagerButton btnScript =
                    buttonInstance.GetComponent<RBS.SleepKit2.Script.PenManagerButton>();
                if (btnScript != null)
                {
                    // Reflectionで取得した pen フィールドから、QvPen_Pen コンポーネントが付いているオブジェクトを取得
                    if (penField != null)
                    {
                        var penValue = penField.GetValue(managerObj);
                        Component penComponent = penValue as Component;
                        if (penComponent != null)
                        {
                            btnScript.SetPen(penComponent.gameObject);
                        }
                        else
                        {
                            Debug.LogWarning(
                                "[PenManagerButtonGenerator] Manager '"
                                    + managerComponent.name
                                    + "' に有効な pen が設定されていません。"
                            );
                        }
                    }
                    else
                    {
                        Debug.LogWarning(
                            "[PenManagerButtonGenerator] 'pen' フィールドが "
                                + managerComponent.name
                                + " に見つかりません。"
                        );
                    }

                    btnScript.SetTeleportTarget(teleportTarget);
                }

                Undo.RegisterCreatedObjectUndo(buttonInstance, "Create Pen Button");
                createdCount++;
            }

            Debug.Log("[PenManagerButtonGenerator] ボタンを " + createdCount + " 個生成しました。");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// <summary>
        /// 指定の Gradient から横1ピクセルのテクスチャを生成します。
        /// </summary>
        private static Texture2D GenerateGradientTexture(Gradient gradient, int resolution)
        {
            Texture2D tex = new Texture2D(resolution, 1, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            for (int i = 0; i < resolution; i++)
            {
                float t = (float)i / (resolution - 1);
                Color col = gradient.Evaluate(t);
                tex.SetPixel(i, 0, col);
            }
            tex.Apply();
            return tex;
        }
    }
}
#endif
