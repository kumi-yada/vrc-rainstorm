using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UCS;
using VRC.SDK3.Persistence;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
using UnityEditor.AnimatedValues;
#endif

namespace UCS
{
    public class UdonChipsAutoGain : UdonChipsOfflineModuleBase
    {
        [SerializeField]
        private UdonChips udonChips;

        [Header("[Basic Settings / 基本設定]")]
        [Min(0f)]
        [SerializeField] float gain = 0.1f;
        [SerializeField] float gainMax = 100000;
        [SerializeField] float interval = 0f;

        [Header("[Offline Settings / オフライン設定]")]
        public bool useWhileOnline = true;
        public bool useWhileOffline = false;

        [Header("[Save Settings / セーブ設定]")]
        public bool saveGain = false;
        public bool saveGainMax = false;
        public string saveKeyGain = "_UDON_CHIPS/AUTO_GAIN/GAIN";
        public string saveKeyGainMax = "_UDON_CHIPS/AUTO_GAIN/GAIN_MAX";

        private float _timeRemain = 0f;
        private float _lastGainPerSec;
        private float _lastGainMax;

        void Start()
        {
            if (udonChips == null)
            {
                udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();
            }
        }

        private void Update()
        {
            if (!useWhileOnline)
            {
                return;
            }

            // AutoGain以外の方法でMoneyを変更している場合は、AutoGainの上限を突破することがある。その状態を保持したまま、所持金の自動増加を停止する。
            if (gain > 0f && udonChips.money < gainMax)
            {
                if (interval <= 0f)
                {
                    udonChips.money += gain * Time.deltaTime;
                    udonChips.money = Mathf.Min(udonChips.money, gainMax);
                }
                else
                {
                    _timeRemain += Time.deltaTime;
                    if (_timeRemain >= interval)
                    {
                        udonChips.money += gain;
                        udonChips.money = Mathf.Min(udonChips.money, gainMax);
                        _timeRemain -= interval;
                    }
                }
            }
        }

        public override void OnPreSaveUdonChips(UdonChipsOffline offline)
        {
            if (udonChips.saveMode != UdonChipsSaveMode.Off)
            {
                if (_lastGainPerSec != gain)
                {
                    PlayerData.SetFloat(saveKeyGain, gain);
                    _lastGainPerSec = gain;
                }

                if (_lastGainMax != gainMax)
                {
                    PlayerData.SetFloat(saveKeyGainMax, gainMax);
                    _lastGainMax = gainMax;
                }
            }
        }

        public override void OnPostLoadUdonChips(UdonChipsOffline offline)
        {
            if (!useWhileOffline)
            {
                return;
            }

            var localPlayer = Networking.LocalPlayer;

            if (saveGain && PlayerData.HasKey(localPlayer, saveKeyGain))
            {
                gain = PlayerData.GetFloat(localPlayer, saveKeyGain);
            }

            if (saveGainMax && PlayerData.HasKey(localPlayer, saveKeyGainMax))
            {
                gainMax = PlayerData.GetFloat(localPlayer, saveKeyGainMax);
            }

            float timePassed = (float)(offline.loadedTime - offline.savedTime).TotalSeconds;

            if (timePassed > 0f)
            {
                if (interval <= 0f)
                {
                    offline.loadedMoney += gain * timePassed;
                    offline.loadedMoney = Mathf.Min(offline.loadedMoney, gainMax);
                }
                else
                {
                    int count = (int)(timePassed / interval);
                    offline.loadedMoney += gain * count;
                    offline.loadedMoney = Mathf.Min(offline.loadedMoney, gainMax);
                }
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void Reset()
        {
            udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();
        }

        public void UdonChipsAutoSet()
        {
            var foundObject = GameObject.Find("UdonChips");
            if (foundObject == null || foundObject.Equals(null))
            {
                Debug.LogWarning("UdonChipsAutoGain: UdonChipsがシーンに配置されていません");
            }
            else
            {
                udonChips = foundObject.GetComponent<UdonChips>();
                Debug.Log("UdonChipsAutoGain: UdonChipsを自動設定しました");
            }
        }

        [CustomEditor(typeof(UdonChipsAutoGain))]
        public class Inspector : Editor
        {
            private AnimBool _foldoutAdvancedSettings = new AnimBool();

            private const string ADVANCED_SETTINGS_FIRST_PROPERTY_NAME = "useWhileOnline";

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
                var target = (UdonChipsAutoGain)base.target;

                serializedObject.Update();
                var prop = serializedObject.GetIterator();

                // Basing Settings を描画 (Advance Settingsの最初のプロパティ名まで続ける)
                while (prop.NextVisible(true) && prop.name != ADVANCED_SETTINGS_FIRST_PROPERTY_NAME)
                {
                    EditorGUILayout.PropertyField(prop);
                }

                // Gain が 0 の場合
                if (target.gain == 0f)
                {
                    EditorGUILayout.HelpBox(
                        $"日本語:\nGain が 0 のとき、Money は自動で増減しません。\n\n" +
                        $"English:\nWhen Gain is 0, Money does not automatically increase or decrease.",
                        MessageType.None
                    );
                }
                else
                {
                    // Gain が 0 ではない場合
                    if (target.interval <= 0f)
                    {
                        EditorGUILayout.HelpBox(
                            $"日本語:\nInterval が 0以下 のとき、1秒あたり Gain で指定した {target.gain} だけ Money が増えます。\n" +
                            $"Money が 0 から Gain Max の値になるまで、推定 {target.gainMax / target.gain} 秒かかります。\n\n" +
                            $"English:\nWhen Interval is less than or equal to 0, Money increases by {target.gain} per second, as specified by Gain.\n" +
                            $"It is estimated that it will take about {target.gainMax / target.gain} seconds for Money to go from 0 to the Gain Max value.",
                            MessageType.None
                        );
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            $"日本語:\nInterval で指定した {target.interval} 秒ごとに、 Gain で指定した {target.gain} だけ Money が増えます。\n" +
                            $"Money が 0 から Gain Max の値になるまで、推定 {target.gainMax / (target.gain / target.interval)} 秒かかります。\n\n" +
                            $"English:\nMoney increases by {target.gain} every {target.interval} seconds, as specified by Interval.\n" +
                            $"It is estimated that it will take about {target.gainMax / (target.gain / target.interval)} seconds for Money to go from 0 to the Gain Max value.",
                            MessageType.None
                        );
                    }
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