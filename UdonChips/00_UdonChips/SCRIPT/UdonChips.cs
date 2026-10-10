using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Persistence;
using System;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor.AnimatedValues;
using UnityEditor;
#endif

namespace UCS
{
    public enum UdonChipsSaveMode
    {
        [InspectorName("Off - セーブ機能無効")]
        [Tooltip("日本語:\n自動セーブ／ロードは無効。\n手動セーブ／ロードも使用不可。\n\nEnglish:\nAuto Save/Load is disabled.\nManual Save/Load methods are locked.")]
        Off = 0,

        [InspectorName("Auto - 自動セーブ")]
        [Tooltip("Japanese:\n自動セーブ／ロードが有効。\nワールド入退出時に所持金をロード／セーブします。\n手動セーブ／ロードも使用可能。\n\nEnglish:\nAuto Save/Load is enabled.\n'Save money' on world exit, 'Load money' on world entry.\nManual Save/Load methods are also enabled.")]
        Auto = 1,

        [InspectorName("Manual (for Developer) - 手動セーブ (開発者用)")]
        [Tooltip("Japanese:\n[注意] ワールド開発者向け。\n自動セーブ／ロードは無効。\n手動セーブ／ロードが有効。\n\nEnglish:\n[WARNING] For world developers only.\nAuto Save/Load is disabled.\nManual Save/Load methods are enabled.")]
        Manual = 2,
    }

    public class UdonChips : UdonSharpBehaviour
    {
        [Header("[基本設定 / Basic Settings]")]
        public float initialMoney = 1000;
        [HideInInspector] public float money = 1000;

        [Tooltip("日本語:\n所持金の表示フォーマット。\n\nEnglish:\nDisplay format for the money value.")]
        public string format = "$ {0:F0}";

        [Tooltip("日本語:\nセーブ機能を選択します。\n\nEnglish:\nSelect the save mode.")]
        public UdonChipsSaveMode saveMode = UdonChipsSaveMode.Off;

        public UdonChipsOffline offline;

        public float autoSaveIntervalSeconds = 10f;

        [Tooltip("日本語:\nセーブデータのキー。\n'UDON_CHIPS/*'形式のキーは公式機能が使用します。\n\nEnglish:\nKey for saving data.\nKeys in the format 'UDON_CHIPS/*' are reserved for official features.")]
        public string saveDataKeyMoney = "_UDON_CHIPS/MONEY";
        public string saveDataKeyLastSaveTime = "_UDON_CHIPS/LAST_SAVE_TIME";

        private float _lastAutoSaveTime;
        private bool _initialLoadCompleted = false;
        private float _lastUpdateMoney;
        private float _lastSavedMoney;
        private DateTime _loadTime;

        public void Start()
        {
            if (saveMode == UdonChipsSaveMode.Off)
            {
                money = initialMoney;
            }
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            switch (saveMode)
            {
                case UdonChipsSaveMode.Auto:
                    if (player.isLocal)
                    {
                        LoadData();
                        _initialLoadCompleted = true;
                    }
                    break;
            }
        }

        private void LoadData()
        {
            VRCPlayerApi player = Networking.LocalPlayer;
            DateTime utcNow = DateTime.UtcNow;
            // セーブデータを読み込む
            if (!PlayerData.TryGetFloat(player, saveDataKeyMoney, out float loadedMoney))
            {
                // セーブデータがないとき、デフォルト値を使用
                loadedMoney = initialMoney;
            }
            money = loadedMoney;

            // セーブ時刻
            if (!PlayerData.TryGetString(player, saveDataKeyLastSaveTime, out string lastSaveTime))
            {
                // セーブデータがないとき、デフォルト値を使用
                _loadTime = utcNow;
            }
            else
            {
                _loadTime = DateTime.Parse(lastSaveTime);
            }
            if (offline != null)
            {
                offline.OnPostLoad(this, loadedMoney, _loadTime, utcNow);
                offline.OnLoadComplete(this);
            }
        }

        private void SaveData()
        {
            if (offline != null)
            {
                offline.OnPreSave(this);
            }

            PlayerData.SetFloat(saveDataKeyMoney, money);
            PlayerData.SetString(saveDataKeyLastSaveTime, System.DateTime.UtcNow.ToString("yyyy/MM/dd HH:mm:ss"));
            _lastSavedMoney = money;

            if (offline != null)
            {
                offline.OnPostSave(this);
            }
        }

        private void Update()
        {
            // 備考: クライアントクラッシュ対策のためUpdate内でセーブをチェック
            // Note: Save in Update as a fallback for client crashes.
            if (saveMode == UdonChipsSaveMode.Auto && _lastSavedMoney != money && _initialLoadCompleted)
            {
                float currentTime = Time.time;

                if (currentTime - _lastAutoSaveTime > autoSaveIntervalSeconds)
                {
                    _lastAutoSaveTime = currentTime;
                    SaveData();
                }
            }

            if (_lastUpdateMoney != money)
            {
                _lastUpdateMoney = money;
                OnMoneyChanged();
            }
        }

        private void OnMoneyChanged()
        {
            SendCustomEvent("OnMoneyChanged");
        }

        [ContextMenu("UdonChips/Manual Load")]
        public void ManualLoad()
        {
            if (saveMode != UdonChipsSaveMode.Off)
            {
                LoadData();
            }
        }

        [ContextMenu("UdonChips/Manual Save")]
        public void ManualSave()
        {
            // 手動セーブ
            if (saveMode != UdonChipsSaveMode.Off)
            {
                SaveData();
            }
        }

        [ContextMenu("UdonChips/Manual Reset")]
        public void ManualReset()
        {
            if (saveMode != UdonChipsSaveMode.Off)
            {
                money = initialMoney;
                SaveData();
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR

        [CustomEditor(typeof(UdonChips))]
        public class Inspector : Editor
        {
            private AnimBool _foldoutAdvancedSettings = new AnimBool();

            private const string ADVANCED_SETTINGS_FIRST_PROPERTY_NAME = "autoSaveIntervalSeconds";

            private void OnEnable()
            {
                _foldoutAdvancedSettings.value = false;
                _foldoutAdvancedSettings.valueChanged.AddListener(Repaint);
            }

            private void OnDisable()
            {
                _foldoutAdvancedSettings.valueChanged.RemoveListener(Repaint);
            }

            public override void OnInspectorGUI()
            {
                //base.OnInspectorGUI();
                var target = (UdonChips)base.target;

                serializedObject.Update();
                var prop = serializedObject.GetIterator();

                // Basing Settings を描画 (Advance Settingsの最初のプロパティ名まで続ける)
                while (prop.NextVisible(true) && prop.name != ADVANCED_SETTINGS_FIRST_PROPERTY_NAME)
                {
                    EditorGUILayout.PropertyField(prop);
                }

                switch (target.saveMode)
                {
                    case UdonChipsSaveMode.Off:
                        EditorGUILayout.HelpBox(
                            "日本語:\n" +
                            "Save Mode が Off です。自動セーブは行われません。\n" +
                            "ワールドに入りなおすたびに、初期所持金(Initial Money)でリセットされます。\n\n" +
                            "English:\n" +
                            "Save Mode is Off. No automatic saving is performed.\n" +
                            "Every time you re-enter the world, it resets to the Initial Money amount.",
                            MessageType.Info
                        );
                        break;

                    case UdonChipsSaveMode.Auto:
                        EditorGUILayout.HelpBox(
                            "日本語:\n" +
                            "Save Mode が Auto です。一定時間ごとに、自動セーブされます。\n" +
                            "はじめてワールドに入るときや、セーブデータをリセットしたとき、初期所持金(Initial Money)の値が使われます。\n\n" +
                            "English:\n" +
                            "Save Mode is Auto. Data is automatically saved at regular intervals.\n" +
                            "When you first enter the world or reset the save data, it uses the value of Initial Money.",
                            MessageType.Info
                        );
                        break;

                    case UdonChipsSaveMode.Manual:
                        EditorGUILayout.HelpBox(
                            "日本語:\n" +
                            "警告！Save Mode の Manual は、ワールド開発者向けの設定です。\n" +
                            "この文章の意味が理解できない場合、すぐに Save Mode を他のものに変更してください！\n\n" +
                            "English:\n" +
                            "Warning! Manual Save Mode is intended for world developers.\n" +
                            "If you do not understand this message, please change Save Mode to another setting immediately!",
                            MessageType.Warning
                        );

                        EditorGUILayout.HelpBox(
                            "日本語:\n" +
                            "Save Mode が Manual です。自動セーブは行われません。\n" +
                            "ワールド開発者は、UdonChips の ManualSave / ManualLoad / ManualReset 関数を使ってください。\n\n" +
                            "English:\n" +
                            "Save Mode is Manual. Automatic saving is not performed.\n" +
                            "World developers should use the ManualSave, ManualLoad, or ManualReset functions in UdonChips.",
                            MessageType.Info
                        );
                        break;
                }

                // Advanced Settings を描画
                _foldoutAdvancedSettings.target = EditorGUILayout.Foldout(_foldoutAdvancedSettings.target, "Advanced Settings / 高度な設定");

                if (EditorGUILayout.BeginFadeGroup(_foldoutAdvancedSettings.faded))
                {
                    EditorGUILayout.HelpBox("日本語:\nこの設定は、セーブデータやオフライン時の動作に強く影響します。\n意味が理解できない場合、触わらないでください。\n\nEnglish:\nThese settings significantly affect save data and offline behavior.\nIf you do not fully understand what they do, please do not modify them.", MessageType.Warning);

                    EditorGUILayout.PropertyField(prop);
                    while (prop.NextVisible(true))
                    {
                        //GUILayout.Label(prop.name);
                        EditorGUILayout.PropertyField(prop);
                    }
                }
                EditorGUILayout.EndFadeGroup();

                serializedObject.ApplyModifiedProperties();
            }
        }
#endif
    }
}
