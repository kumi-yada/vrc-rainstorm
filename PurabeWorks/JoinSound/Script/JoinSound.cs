// Copyright (c) 2026 Purabe Works
// Released under the MIT License. See LICENSE for details.
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace PurabeWorks
{
    /// <summary>
    /// プレイヤーがJoinしたときに音を再生する
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class JoinSound : UdonSharpBehaviour
    {
        [SerializeField]
        private AudioSource joinSoundAudioSource;
        [SerializeField]
        private AudioClip joinSoundAudioClip;
        [SerializeField]
        private AudioClip leaveSoundAudioClip;

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.LocalPlayer == null || player == Networking.LocalPlayer) return;

            if (joinSoundAudioSource != null && joinSoundAudioClip != null)
            {
                joinSoundAudioSource.PlayOneShot(joinSoundAudioClip);
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (Networking.LocalPlayer == null || player == Networking.LocalPlayer) return;

            if (joinSoundAudioSource != null && leaveSoundAudioClip != null)
            {
                joinSoundAudioSource.PlayOneShot(leaveSoundAudioClip);
            }
        }
    }
}