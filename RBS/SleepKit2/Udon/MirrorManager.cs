using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace RBS.SleepKit2.Udon
{
    public class MirrorManager : UdonSharpBehaviour
    {
        // キー定義（基本キー）
        private const string KEY_TOP_MIRROR_STATE = "TOP_MIRROR_STATE";
        private const string KEY_TOP_MIRROR_PERSIST = "TOP_MIRROR_PERSIST";
        private const string KEY_FRONT_MIRROR_STATE = "FRONT_MIRROR_STATE";
        private const string KEY_FRONT_MIRROR_PERSIST = "FRONT_MIRROR_PERSIST";
        private const string KEY_LEFT_MIRROR_STATE = "LEFT_MIRROR_STATE";
        private const string KEY_LEFT_MIRROR_PERSIST = "LEFT_MIRROR_PERSIST";
        private const string KEY_RIGHT_MIRROR_STATE = "RIGHT_MIRROR_STATE";
        private const string KEY_RIGHT_MIRROR_PERSIST = "RIGHT_MIRROR_PERSIST";
        private const string KEY_BED_COLLIDER_STATE = "BED_COLLIDER_STATE";
        private const string KEY_BED_COLLIDER_PERSIST = "BED_COLLIDER_PERSIST";

        #region Inspector Fields

        [Header("Persistence Settings")]
        [Tooltip("PersistenceManagerの参照。各Persistenceキーの先頭にこのprefixが付加されます。")]
        public PersistenceManager persistenceManager;

        [Header("Top Mirror (OFF / LQ / HQ) + Toggles")]
        public GameObject[] topMirrorsOFF;
        public GameObject[] topMirrorsLQ;
        public GameObject[] topMirrorsHQ;

        [Tooltip("Top用 ToggleGroups (OFF/LQ/HQ)")]
        public ToggleGroup[] topMirrorGroups;

        [Tooltip("Top用 OFFトグル")]
        public Toggle[] topToggleOFF;

        [Tooltip("Top用 LQトグル")]
        public Toggle[] topToggleLQ;

        [Tooltip("Top用 HQトグル")]
        public Toggle[] topToggleHQ;

        [Tooltip("Top用 Persistenceトグル (永続化ON/OFF)")]
        public Toggle[] topMirrorPersistToggles;

        [Header("Front Mirror (OFF / LQ / HQ) + Toggles")]
        public GameObject[] frontMirrorsOFF;
        public GameObject[] frontMirrorsLQ;
        public GameObject[] frontMirrorsHQ;

        [Tooltip("Front用 ToggleGroups (OFF/LQ/HQ)")]
        public ToggleGroup[] frontMirrorGroups;

        [Tooltip("Front用 OFFトグル")]
        public Toggle[] frontToggleOFF;

        [Tooltip("Front用 LQトグル")]
        public Toggle[] frontToggleLQ;

        [Tooltip("Front用 HQトグル")]
        public Toggle[] frontToggleHQ;
        public Toggle[] frontMirrorPersistToggles;

        [Header("Left Mirror (OFF / LQ / HQ) + Toggles")]
        public GameObject[] leftMirrorsOFF;
        public GameObject[] leftMirrorsLQ;
        public GameObject[] leftMirrorsHQ;

        [Tooltip("Left用 ToggleGroups (OFF/LQ/HQ)")]
        public ToggleGroup[] leftMirrorGroups;

        [Tooltip("Left用 OFFトグル")]
        public Toggle[] leftToggleOFF;

        [Tooltip("Left用 LQトグル")]
        public Toggle[] leftToggleLQ;

        [Tooltip("Left用 HQトグル")]
        public Toggle[] leftToggleHQ;
        public Toggle[] leftMirrorPersistToggles;

        [Header("Right Mirror (OFF / LQ / HQ) + Toggles")]
        public GameObject[] rightMirrorsOFF;
        public GameObject[] rightMirrorsLQ;
        public GameObject[] rightMirrorsHQ;

        [Tooltip("Right用 ToggleGroups (OFF/LQ/HQ)")]
        public ToggleGroup[] rightMirrorGroups;

        [Tooltip("Right用 OFFトグル")]
        public Toggle[] rightToggleOFF;

        [Tooltip("Right用 LQトグル")]
        public Toggle[] rightToggleLQ;

        [Tooltip("Right用 HQトグル")]
        public Toggle[] rightToggleHQ;
        public Toggle[] rightMirrorPersistToggles;

        [Header("Bed Colliders (OFF / ON) + Toggles")]
        public GameObject[] bedCollidersOFF;
        public GameObject[] bedCollidersON;

        [Tooltip("BedCollider用 ToggleGroups (OFF/ON)")]
        public ToggleGroup[] bedColliderGroups;

        [Tooltip("BedCollider用 OFFトグル")]
        public Toggle[] bedToggleOFF;

        [Tooltip("BedCollider用 ONトグル")]
        public Toggle[] bedToggleON;
        public Toggle[] bedColliderPersistToggles;

        #endregion

        #region Private Fields

        // 各ミラーの状態（0: OFF, 1: LQ, 2: HQ）
        private int topMirrorState,
            frontMirrorState,
            leftMirrorState,
            rightMirrorState;

        // 永続化フラグ（trueの場合、最後の状態を維持）
        private bool topMirrorPersist,
            frontMirrorPersist,
            leftMirrorPersist,
            rightMirrorPersist;

        // BedColliderの状態（0: OFF, 1: ON）
        private int bedColliderState;
        private bool bedColliderPersist;

        #endregion

        #region Udon Behaviour Events

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!player.isLocal)
                return;

            // Top Mirror
            topMirrorState = GetPlayerInt(player, GetPrefixedKey(KEY_TOP_MIRROR_STATE), 0);
            topMirrorPersist = GetPlayerBool(player, GetPrefixedKey(KEY_TOP_MIRROR_PERSIST), false);
            TogglePersistenceToggles(topMirrorPersistToggles, topMirrorPersist);
            if (!topMirrorPersist)
            {
                topMirrorState = 0;
                PlayerData.SetInt(GetPrefixedKey(KEY_TOP_MIRROR_STATE), topMirrorState);
            }
            UpdateTopMirror();

            // Front Mirror
            frontMirrorState = GetPlayerInt(player, GetPrefixedKey(KEY_FRONT_MIRROR_STATE), 0);
            frontMirrorPersist = GetPlayerBool(
                player,
                GetPrefixedKey(KEY_FRONT_MIRROR_PERSIST),
                false
            );
            TogglePersistenceToggles(frontMirrorPersistToggles, frontMirrorPersist);
            if (!frontMirrorPersist)
            {
                frontMirrorState = 0;
                PlayerData.SetInt(GetPrefixedKey(KEY_FRONT_MIRROR_STATE), frontMirrorState);
            }
            UpdateFrontMirror();

            // Left Mirror
            leftMirrorState = GetPlayerInt(player, GetPrefixedKey(KEY_LEFT_MIRROR_STATE), 0);
            leftMirrorPersist = GetPlayerBool(
                player,
                GetPrefixedKey(KEY_LEFT_MIRROR_PERSIST),
                false
            );
            TogglePersistenceToggles(leftMirrorPersistToggles, leftMirrorPersist);
            if (!leftMirrorPersist)
            {
                leftMirrorState = 0;
                PlayerData.SetInt(GetPrefixedKey(KEY_LEFT_MIRROR_STATE), leftMirrorState);
            }
            UpdateLeftMirror();

            // Right Mirror
            rightMirrorState = GetPlayerInt(player, GetPrefixedKey(KEY_RIGHT_MIRROR_STATE), 0);
            rightMirrorPersist = GetPlayerBool(
                player,
                GetPrefixedKey(KEY_RIGHT_MIRROR_PERSIST),
                false
            );
            TogglePersistenceToggles(rightMirrorPersistToggles, rightMirrorPersist);
            if (!rightMirrorPersist)
            {
                rightMirrorState = 0;
                PlayerData.SetInt(GetPrefixedKey(KEY_RIGHT_MIRROR_STATE), rightMirrorState);
            }
            UpdateRightMirror();

            // Bed Collider
            bedColliderState = GetPlayerInt(player, GetPrefixedKey(KEY_BED_COLLIDER_STATE), 0);
            bedColliderPersist = GetPlayerBool(
                player,
                GetPrefixedKey(KEY_BED_COLLIDER_PERSIST),
                false
            );
            TogglePersistenceToggles(bedColliderPersistToggles, bedColliderPersist);
            if (!bedColliderPersist)
            {
                bedColliderState = 0;
                PlayerData.SetInt(GetPrefixedKey(KEY_BED_COLLIDER_STATE), bedColliderState);
            }
            UpdateBedCollider();
        }

        #endregion

        #region Helper Methods

        private int GetPlayerInt(VRCPlayerApi player, string key, int defaultValue)
        {
            return PlayerData.HasKey(player, key) ? PlayerData.GetInt(player, key) : defaultValue;
        }

        private bool GetPlayerBool(VRCPlayerApi player, string key, bool defaultValue)
        {
            return PlayerData.HasKey(player, key) ? PlayerData.GetBool(player, key) : defaultValue;
        }

        private void ToggleObjects(GameObject[] objs, bool active)
        {
            if (objs == null)
                return;
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] != null)
                    objs[i].SetActive(active);
            }
        }

        /// <summary>
        /// 永続化トグルは複数個ある前提なので、まとめてセットする
        /// </summary>
        private void TogglePersistenceToggles(Toggle[] toggles, bool isOn)
        {
            if (toggles == null)
                return;
            for (int i = 0; i < toggles.Length; i++)
            {
                if (toggles[i] != null)
                    toggles[i].SetIsOnWithoutNotify(isOn);
            }
        }

        /// <summary>
        /// PersistenceManager が設定されている場合、その prefix を付与したキーを返します。
        /// </summary>
        private string GetPrefixedKey(string baseKey)
        {
            return (persistenceManager != null ? persistenceManager.prefix : "") + baseKey;
        }

        #endregion

        #region Update Methods

        /// <summary>
        /// Top Mirror の状態を更新 (0:OFF / 1:LQ / 2:HQ)
        /// </summary>
        private void UpdateTopMirror()
        {
            // まず、全ToggleGroupのallowSwitchOffをtrueにする
            if (topMirrorGroups != null)
            {
                foreach (ToggleGroup group in topMirrorGroups)
                {
                    if (group != null)
                        group.allowSwitchOff = true;
                }
            }

            // 実際のGameObjectのOn/Off更新
            ToggleObjects(topMirrorsOFF, topMirrorState == 0);
            ToggleObjects(topMirrorsLQ, topMirrorState == 1);
            ToggleObjects(topMirrorsHQ, topMirrorState == 2);

            // 各トグルの見た目を同期
            if (topToggleOFF != null)
            {
                foreach (Toggle t in topToggleOFF)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(topMirrorState == 0);
                }
            }
            if (topToggleLQ != null)
            {
                foreach (Toggle t in topToggleLQ)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(topMirrorState == 1);
                }
            }
            if (topToggleHQ != null)
            {
                foreach (Toggle t in topToggleHQ)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(topMirrorState == 2);
                }
            }

            // 最後に、allowSwitchOffをfalseに戻す
            if (topMirrorGroups != null)
            {
                foreach (ToggleGroup group in topMirrorGroups)
                {
                    if (group != null)
                        group.allowSwitchOff = false;
                }
            }
        }

        private void UpdateFrontMirror()
        {
            if (frontMirrorGroups != null)
            {
                foreach (ToggleGroup group in frontMirrorGroups)
                {
                    if (group != null)
                        group.allowSwitchOff = true;
                }
            }

            ToggleObjects(frontMirrorsOFF, frontMirrorState == 0);
            ToggleObjects(frontMirrorsLQ, frontMirrorState == 1);
            ToggleObjects(frontMirrorsHQ, frontMirrorState == 2);

            if (frontToggleOFF != null)
            {
                foreach (Toggle t in frontToggleOFF)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(frontMirrorState == 0);
                }
            }
            if (frontToggleLQ != null)
            {
                foreach (Toggle t in frontToggleLQ)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(frontMirrorState == 1);
                }
            }
            if (frontToggleHQ != null)
            {
                foreach (Toggle t in frontToggleHQ)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(frontMirrorState == 2);
                }
            }

            if (frontMirrorGroups != null)
            {
                foreach (ToggleGroup group in frontMirrorGroups)
                {
                    if (group != null)
                        group.allowSwitchOff = false;
                }
            }
        }

        private void UpdateLeftMirror()
        {
            if (leftMirrorGroups != null)
            {
                foreach (ToggleGroup group in leftMirrorGroups)
                {
                    if (group != null)
                        group.allowSwitchOff = true;
                }
            }

            ToggleObjects(leftMirrorsOFF, leftMirrorState == 0);
            ToggleObjects(leftMirrorsLQ, leftMirrorState == 1);
            ToggleObjects(leftMirrorsHQ, leftMirrorState == 2);

            if (leftToggleOFF != null)
            {
                foreach (Toggle t in leftToggleOFF)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(leftMirrorState == 0);
                }
            }
            if (leftToggleLQ != null)
            {
                foreach (Toggle t in leftToggleLQ)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(leftMirrorState == 1);
                }
            }
            if (leftToggleHQ != null)
            {
                foreach (Toggle t in leftToggleHQ)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(leftMirrorState == 2);
                }
            }

            if (leftMirrorGroups != null)
            {
                foreach (ToggleGroup group in leftMirrorGroups)
                {
                    if (group != null)
                        group.allowSwitchOff = false;
                }
            }
        }

        private void UpdateRightMirror()
        {
            if (rightMirrorGroups != null)
            {
                foreach (ToggleGroup group in rightMirrorGroups)
                {
                    if (group != null)
                        group.allowSwitchOff = true;
                }
            }

            ToggleObjects(rightMirrorsOFF, rightMirrorState == 0);
            ToggleObjects(rightMirrorsLQ, rightMirrorState == 1);
            ToggleObjects(rightMirrorsHQ, rightMirrorState == 2);

            if (rightToggleOFF != null)
            {
                foreach (Toggle t in rightToggleOFF)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(rightMirrorState == 0);
                }
            }
            if (rightToggleLQ != null)
            {
                foreach (Toggle t in rightToggleLQ)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(rightMirrorState == 1);
                }
            }
            if (rightToggleHQ != null)
            {
                foreach (Toggle t in rightToggleHQ)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(rightMirrorState == 2);
                }
            }

            if (rightMirrorGroups != null)
            {
                foreach (ToggleGroup group in rightMirrorGroups)
                {
                    if (group != null)
                        group.allowSwitchOff = false;
                }
            }
        }

        private void UpdateBedCollider()
        {
            if (bedColliderGroups != null)
            {
                foreach (ToggleGroup group in bedColliderGroups)
                {
                    if (group != null)
                        group.allowSwitchOff = true;
                }
            }

            ToggleObjects(bedCollidersOFF, bedColliderState == 0);
            ToggleObjects(bedCollidersON, bedColliderState == 1);

            if (bedToggleOFF != null)
            {
                foreach (Toggle t in bedToggleOFF)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(bedColliderState == 0);
                }
            }
            if (bedToggleON != null)
            {
                foreach (Toggle t in bedToggleON)
                {
                    if (t != null)
                        t.SetIsOnWithoutNotify(bedColliderState == 1);
                }
            }

            if (bedColliderGroups != null)
            {
                foreach (ToggleGroup group in bedColliderGroups)
                {
                    if (group != null)
                        group.allowSwitchOff = false;
                }
            }
        }

        #endregion

        #region Public Methods (Mirror Persistence Actions)
        //=== Top Mirror ===
        public void TopMirrorOff()
        {
            topMirrorState = 0;
            UpdateTopMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_TOP_MIRROR_STATE), topMirrorState);
        }

        public void TopMirrorLQ()
        {
            topMirrorState = 1;
            UpdateTopMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_TOP_MIRROR_STATE), topMirrorState);
        }

        public void TopMirrorHQ()
        {
            topMirrorState = 2;
            UpdateTopMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_TOP_MIRROR_STATE), topMirrorState);
        }

        public void ToggleTopMirrorPersistence()
        {
            topMirrorPersist = !topMirrorPersist;
            TogglePersistenceToggles(topMirrorPersistToggles, topMirrorPersist);
            PlayerData.SetBool(GetPrefixedKey(KEY_TOP_MIRROR_PERSIST), topMirrorPersist);

            if (!topMirrorPersist)
            {
                // 非永続ならOFFに戻す
                topMirrorState = 0;
                PlayerData.SetInt(GetPrefixedKey(KEY_TOP_MIRROR_STATE), topMirrorState);
            }
            UpdateTopMirror();
        }

        //=== Front Mirror ===
        public void FrontMirrorOff()
        {
            frontMirrorState = 0;
            UpdateFrontMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_FRONT_MIRROR_STATE), frontMirrorState);
        }

        public void FrontMirrorLQ()
        {
            frontMirrorState = 1;
            UpdateFrontMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_FRONT_MIRROR_STATE), frontMirrorState);
        }

        public void FrontMirrorHQ()
        {
            frontMirrorState = 2;
            UpdateFrontMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_FRONT_MIRROR_STATE), frontMirrorState);
        }

        public void ToggleFrontMirrorPersistence()
        {
            frontMirrorPersist = !frontMirrorPersist;
            TogglePersistenceToggles(frontMirrorPersistToggles, frontMirrorPersist);
            PlayerData.SetBool(GetPrefixedKey(KEY_FRONT_MIRROR_PERSIST), frontMirrorPersist);

            if (!frontMirrorPersist)
            {
                frontMirrorState = 0;
                PlayerData.SetInt(GetPrefixedKey(KEY_FRONT_MIRROR_STATE), frontMirrorState);
            }
            UpdateFrontMirror();
        }

        //=== Left Mirror ===
        public void LeftMirrorOff()
        {
            leftMirrorState = 0;
            UpdateLeftMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_LEFT_MIRROR_STATE), leftMirrorState);
        }

        public void LeftMirrorLQ()
        {
            leftMirrorState = 1;
            UpdateLeftMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_LEFT_MIRROR_STATE), leftMirrorState);
        }

        public void LeftMirrorHQ()
        {
            leftMirrorState = 2;
            UpdateLeftMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_LEFT_MIRROR_STATE), leftMirrorState);
        }

        public void ToggleLeftMirrorPersistence()
        {
            leftMirrorPersist = !leftMirrorPersist;
            TogglePersistenceToggles(leftMirrorPersistToggles, leftMirrorPersist);
            PlayerData.SetBool(GetPrefixedKey(KEY_LEFT_MIRROR_PERSIST), leftMirrorPersist);

            if (!leftMirrorPersist)
            {
                leftMirrorState = 0;
                PlayerData.SetInt(GetPrefixedKey(KEY_LEFT_MIRROR_STATE), leftMirrorState);
            }
            UpdateLeftMirror();
        }

        //=== Right Mirror ===
        public void RightMirrorOff()
        {
            rightMirrorState = 0;
            UpdateRightMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_RIGHT_MIRROR_STATE), rightMirrorState);
        }

        public void RightMirrorLQ()
        {
            rightMirrorState = 1;
            UpdateRightMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_RIGHT_MIRROR_STATE), rightMirrorState);
        }

        public void RightMirrorHQ()
        {
            rightMirrorState = 2;
            UpdateRightMirror();
            PlayerData.SetInt(GetPrefixedKey(KEY_RIGHT_MIRROR_STATE), rightMirrorState);
        }

        public void ToggleRightMirrorPersistence()
        {
            rightMirrorPersist = !rightMirrorPersist;
            TogglePersistenceToggles(rightMirrorPersistToggles, rightMirrorPersist);
            PlayerData.SetBool(GetPrefixedKey(KEY_RIGHT_MIRROR_PERSIST), rightMirrorPersist);

            if (!rightMirrorPersist)
            {
                rightMirrorState = 0;
                PlayerData.SetInt(GetPrefixedKey(KEY_RIGHT_MIRROR_STATE), rightMirrorState);
            }
            UpdateRightMirror();
        }

        //=== BedCollider ===
        public void BedColliderOff()
        {
            bedColliderState = 0;
            UpdateBedCollider();
            PlayerData.SetInt(GetPrefixedKey(KEY_BED_COLLIDER_STATE), bedColliderState);
        }

        public void BedColliderOn()
        {
            bedColliderState = 1;
            UpdateBedCollider();
            PlayerData.SetInt(GetPrefixedKey(KEY_BED_COLLIDER_STATE), bedColliderState);
        }

        public void ToggleBedColliderPersistence()
        {
            bedColliderPersist = !bedColliderPersist;
            TogglePersistenceToggles(bedColliderPersistToggles, bedColliderPersist);
            PlayerData.SetBool(GetPrefixedKey(KEY_BED_COLLIDER_PERSIST), bedColliderPersist);

            if (!bedColliderPersist)
            {
                bedColliderState = 0; // 状態をリセット
                PlayerData.SetInt(GetPrefixedKey(KEY_BED_COLLIDER_STATE), bedColliderState);
            }
            UpdateBedCollider();
        }

        #endregion
    }
}
