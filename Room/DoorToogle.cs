
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class DoorToogle : UdonSharpBehaviour
{
    public Transform targetTransform;
    public float moveDuration = 0.4f;

    [Header("Door Sound (Optional)")]
    [Tooltip("AudioSource for door open/close sound effects")]
    public AudioSource doorAudio;

    [Tooltip("Clip played when the door opens")]
    public AudioClip openClip;

    [Tooltip("Clip played when the door closes")]
    public AudioClip closeClip;

    [UdonSynced] private bool syncedOpen;

    private Vector3 originalPosition;
    private bool isOpen;
    private bool isMoving;
    private float moveStartTime;
    private Vector3 moveFrom;
    private Vector3 moveTo;

    void Start()
    {
        originalPosition = transform.localPosition;
    }

    public override void Interact()
    {
        if (isMoving)
        {
            return;
        }

        Networking.SetOwner(Networking.LocalPlayer, gameObject);

        isOpen = !isOpen;
        syncedOpen = isOpen;
        RequestSerialization();

        BeginMove();
        PlayDoorSound();
    }

    public override void OnDeserialization()
    {
        isOpen = syncedOpen;
        BeginMove();
        PlayDoorSound();
    }

    private void PlayDoorSound()
    {
        if (doorAudio == null)
        {
            return;
        }

        AudioClip clip = isOpen ? openClip : closeClip;
        if (clip != null)
        {
            doorAudio.PlayOneShot(clip);
        }
    }

    private void BeginMove()
    {
        moveFrom = transform.localPosition;
        moveTo = isOpen ? GetTargetPosition() : originalPosition;
        moveStartTime = Time.time;
        isMoving = true;
    }

    private Vector3 GetTargetPosition()
    {
        if (targetTransform == null)
        {
            return originalPosition;
        }

        Vector3 targetPosition = originalPosition;
        targetPosition.z = targetTransform.localPosition.z;
        return targetPosition;
    }

    void Update()
    {
        if (!isMoving)
        {
            return;
        }

        float t = (Time.time - moveStartTime) / moveDuration;
        if (t >= 1f)
        {
            t = 1f;
            isMoving = false;
        }

        float eased = 1f - Mathf.Pow(1f - t, 3f);
        transform.localPosition = Vector3.Lerp(moveFrom, moveTo, eased);
    }
}
