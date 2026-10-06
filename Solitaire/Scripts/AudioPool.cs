using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

// Shared pool of AudioSources for short one-shot sounds. A source is chosen
// round-robin, moved to the requested world position, and played with
// PlayOneShot, so overlapping cues mix instead of cutting each other off and no
// source is ever "busy".
//
// The sources are positioned for spatial playback: set each AudioSource's
// Spatial Blend to 1 and add a VRC_SpatialAudioSource to hear them in 3D.
//
// Playback can be local-only (PlayLocal / PlayLocalIndex) or networked
// (PlayNetworked / PlayRemote). A network play cannot carry an AudioClip
// reference, so the clip travels as an index into the Clips list and every
// client resolves it against its own copy of that list. The position travels
// with the network call so the sound is spatialised the same way everywhere.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class AudioPool : UdonSharpBehaviour
{
    [Tooltip("Sources the clips are played through. Left empty, AudioPool fills it at startup from the AudioSources on its children (see Find Sources In Children).")]
    [SerializeField] private AudioSource[] sources;

    [Tooltip("When Sources is empty, gather the AudioSources from this GameObject and its children at startup instead of requiring them to be assigned by hand.")]
    [SerializeField] private bool findSourcesInChildren = true;

    [Tooltip("Clips a network play may reference by index. Every clip passed to a networked method must also be listed here, or the remote clients cannot resolve it and stay silent.")]
    [SerializeField] private AudioClip[] clips;

    // Rotates through sources so consecutive sounds land on different ones.
    private int nextSource;

    private void Start()
    {
        if ((sources == null || sources.Length == 0) && findSourcesInChildren)
        {
            // Inactive children included: a pool may park its sources disabled.
            sources = GetComponentsInChildren<AudioSource>(true);
        }
    }

    // Plays on this client only.
    public void PlayLocal(AudioClip clip, Vector3 position)
    {
        PlayOnPool(clip, position);
    }

    // Plays on this client only, addressed by index into Clips.
    public void PlayLocalIndex(int clipIndex, Vector3 position)
    {
        PlayOnPool(GetClip(clipIndex), position);
    }

    // Plays here immediately, then on every other client. The clip must be in
    // the Clips list for the remote copies to resolve it.
    public void PlayNetworked(AudioClip clip, Vector3 position)
    {
        int index = IndexOf(clip);
        if (index < 0)
        {
            PlayOnPool(clip, position);
            return;
        }
        PlayNetworkedIndex(index, position);
    }

    // Plays on every other client, but not here. The clip must be in the Clips
    // list for the remote copies to resolve it.
    public void PlayRemote(AudioClip clip, Vector3 position)
    {
        int index = IndexOf(clip);
        if (index < 0) return;
        PlayRemoteIndex(index, position);
    }

    // Same as PlayNetworked, addressed by index into Clips. This is the variant
    // to use when the caller stores indices rather than clip references.
    public void PlayNetworkedIndex(int clipIndex, Vector3 position)
    {
        AudioClip clip = GetClip(clipIndex);
        if (clip == null) return;
        PlayOnPool(clip, position);
        SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(_PlayRemoteIndex), clipIndex, position);
    }

    // Same as PlayRemote, addressed by index into Clips.
    public void PlayRemoteIndex(int clipIndex, Vector3 position)
    {
        if (GetClip(clipIndex) == null) return;
        SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(_PlayRemoteIndex), clipIndex, position);
    }

    // Network entry point. Exposed to remote callers by the attribute; local
    // callers should use the public methods above instead.
    [NetworkCallable(30)]
    public void _PlayRemoteIndex(int clipIndex, Vector3 position)
    {
        if (!NetworkCalling.InNetworkCall) return;

        VRCPlayerApi caller = NetworkCalling.CallingPlayer;
        if (caller == null || !caller.IsValid()) return;

        PlayOnPool(GetClip(clipIndex), position);
    }

    private int IndexOf(AudioClip clip)
    {
        if (clip == null || clips == null) return -1;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == clip) return i;
        }
        return -1;
    }

    private AudioClip GetClip(int clipIndex)
    {
        if (clips == null || clipIndex < 0 || clipIndex >= clips.Length) return null;
        return clips[clipIndex];
    }

    private void PlayOnPool(AudioClip clip, Vector3 position)
    {
        if (clip == null || sources == null) return;
        int count = sources.Length;
        if (count == 0) return;

        for (int attempt = 0; attempt < count; attempt++)
        {
            AudioSource source = sources[nextSource];
            nextSource = (nextSource + 1) % count;
            if (source == null) continue;
            source.transform.position = position;
            source.PlayOneShot(clip);
            return;
        }
    }
}
