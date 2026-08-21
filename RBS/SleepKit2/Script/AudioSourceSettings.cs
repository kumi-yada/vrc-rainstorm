using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioSourceSettings : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip audioClip;
    public bool loop = false;
    public float volume = 1.0f;
    public float pitch = 1.0f;

    private void OnValidate()
    {
        if (audioSource != null)
        {
            audioSource.clip = audioClip;
            audioSource.loop = loop;
            audioSource.volume = volume;
            audioSource.pitch = pitch;
        }
    }
}
