using UnityEngine;

[RequireComponent(typeof(Camera)), DisallowMultipleComponent]
public sealed class DeadZoneCamera2D : MonoBehaviour
{
    [SerializeField] private Transform target;
    [Tooltip("Half-width in world units")]
    [SerializeField, Min(0)] private float horizontalDeadZone = 3f;
    [Tooltip("Half-height in world units")]
    [SerializeField, Min(0)] private float verticalDeadZone = 1.8f;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.25f;
    [SerializeField] private bool followHorizontal = true, followVertical = true;
    private Vector3 velocity;

    private void LateUpdate()
    {
        if (!target || !target.gameObject.activeInHierarchy) { velocity = Vector3.zero; return; }
        var current = transform.position;
        var delta = target.position - current;
        // Reset each axis inside the buffer: residual SmoothDamp velocity must not cause drift.
        float x = followHorizontal ? delta.x - Mathf.Clamp(delta.x, -horizontalDeadZone, horizontalDeadZone) : 0;
        float y = followVertical ? delta.y - Mathf.Clamp(delta.y, -verticalDeadZone, verticalDeadZone) : 0;
        if (Mathf.Abs(x) < 0.0001f) velocity.x = 0;
        else current.x = Mathf.SmoothDamp(current.x, current.x + x, ref velocity.x, smoothTime);
        if (Mathf.Abs(y) < 0.0001f) velocity.y = 0;
        else current.y = Mathf.SmoothDamp(current.y, current.y + y, ref velocity.y, smoothTime);
        transform.position = current;
    }

    public void ResetAfterRespawn(Vector2 position)
    {
        velocity = Vector3.zero;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position + Vector3.forward * 10, new Vector3(horizontalDeadZone * 2, verticalDeadZone * 2, 0));
    }
}
