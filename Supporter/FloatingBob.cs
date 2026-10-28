using UdonSharp;
using UnityEngine;

// Makes an object bob up and down with a sine wave, in parent-local space.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class FloatingBob : UdonSharpBehaviour
{
    [Tooltip("Object to float up and down. Falls back to this GameObject's transform if empty.")]
    [SerializeField] private Transform floatTarget;

    [Tooltip("Float speed multiplier.")]
    [SerializeField] private float floatSpeed = 3f;

    [Tooltip("How far up and down the object travels, in parent-local units.")]
    [SerializeField] private float floatAmount = 0.1f;

    private Transform _bobTransform;
    private Vector3 _bobStartLocalPos;

    void Start()
    {
        _bobTransform = floatTarget != null ? floatTarget : transform;
        _bobStartLocalPos = _bobTransform.localPosition;
    }

    void Update()
    {
        if (_bobTransform == null)
        {
            return;
        }

        float y = Mathf.Sin(Time.time * floatSpeed) * floatAmount;
        _bobTransform.localPosition = _bobStartLocalPos + Vector3.up * y;
    }
}
