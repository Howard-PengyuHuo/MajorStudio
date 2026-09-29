using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class DogController2D : MonoBehaviour
{
    public enum SurfaceMode { Normal, Inverted, Released }

    [Header("Movement")]
    [SerializeField, Min(0)] private float moveSpeed = 4f;
    [SerializeField, Min(0)] private float jumpForce = 6f;
    [SerializeField, Min(0.01f)] private float gravityScale = 3f;
    [Header("Surfaces")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField, Min(0.01f)] private float groundCheckDistance = 0.06f;
    [SerializeField, Min(0.001f)] private float surfaceOffset = 0.025f;
    [SerializeField, Min(0)] private float flipCooldown = 0.15f;
    [Tooltip("Maximum thickness of a box that Flip can cross.")]
    [SerializeField, Min(0.1f)] private float flipProbeDistance = 10f;
    [Header("Keys (project uses Both input backends)")]
    [SerializeField] private KeyCode flipKey = KeyCode.LeftShift;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [Header("Existing Animator")]
    [Tooltip("Existing controller has states but no transitions. Disable when using parameter-driven transitions.")]
    [SerializeField] private bool playExistingStates = true;
    [SerializeField] private string idleState = "New Layer.Idle";
    [SerializeField] private string runState = "New Layer.Run";
    [SerializeField] private string jumpState = "New Layer.Jump";
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string groundedParameter = "IsGrounded";
    [SerializeField] private bool spriteFacesRight = true;

    public SurfaceMode Mode { get; private set; }
    public bool IsGrounded => attachedSurface != null;
    public Collider2D AttachedSurface => attachedSurface;
    private Rigidbody2D body;
    private BoxCollider2D shape;
    private SpriteRenderer visual;
    private Animator animator;
    private Collider2D attachedSurface;
    private float horizontal, nextFlipTime;
    private bool jumpRequested, flipRequested, releaseRequested;
    private bool facingRight = true, hasSpeed, hasGrounded;
    private int playingState;
    private readonly RaycastHit2D[] hits = new RaycastHit2D[32];
    private Vector2 GravityDirection => Mode == SurfaceMode.Inverted ? Vector2.up : Vector2.down;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        shape = GetComponent<BoxCollider2D>();
        visual = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        body.constraints |= RigidbodyConstraints2D.FreezeRotation;
        Mode = SurfaceMode.Normal;
        body.rotation = 0;
        ApplyGravity();
        if (animator)
            foreach (var parameter in animator.parameters)
            {
                hasSpeed |= parameter.name == speedParameter && parameter.type == AnimatorControllerParameterType.Float;
                hasGrounded |= parameter.name == groundedParameter && parameter.type == AnimatorControllerParameterType.Bool;
            }
    }

    private void Update()
    {
        horizontal = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0)
                   - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
        jumpRequested |= Input.GetKeyDown(jumpKey);
        flipRequested |= Input.GetKeyDown(flipKey);
        releaseRequested |= Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
    }

    private void FixedUpdate()
    {
        DetectSurface();
        body.linearVelocity = new Vector2(horizontal * moveSpeed, body.linearVelocity.y);
        if (releaseRequested && Mode == SurfaceMode.Inverted) Release();
        else if (flipRequested && TryFlip()) { }
        else if (jumpRequested && IsGrounded)
        {
            // Away from the supporting surface, in either orientation.
            body.linearVelocity = new Vector2(body.linearVelocity.x, -GravityDirection.y * jumpForce);
            attachedSurface = null;
        }
        jumpRequested = flipRequested = releaseRequested = false;
    }

    private void DetectSurface()
    {
        attachedSurface = null;
        // A velocity guard prevents re-grounding during takeoff; the cast finds actual support.
        if (Vector2.Dot(body.linearVelocity, GravityDirection) < -0.1f) return;
        var filter = new ContactFilter2D();
        filter.SetLayerMask(groundLayer);
        filter.useTriggers = false;
        int count = shape.Cast(GravityDirection, filter, hits, groundCheckDistance);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider.attachedRigidbody == body || hit.distance >= nearest) continue;
            if (Vector2.Dot(hit.normal, -GravityDirection) < 0.95f) continue;
            // Require the center above/below the surface, avoiding corner-only flips.
            if (shape.bounds.center.x < hit.collider.bounds.min.x || shape.bounds.center.x > hit.collider.bounds.max.x) continue;
            nearest = hit.distance;
            attachedSurface = hit.collider;
        }
        if (IsGrounded && Mode == SurfaceMode.Released)
        {
            Mode = SurfaceMode.Normal;
            // Only rotate upright AFTER landing.
            body.rotation = 0f;

            ApplyGravity();
        }
    }

    private bool TryFlip()
    {
        if (!IsGrounded || Time.time < nextFlipTime || !(attachedSurface is BoxCollider2D terrain)) return false;
        // First prototype: static, axis-aligned, solid rectangles only.
        if (terrain.usedByEffector || terrain.edgeRadius > 0 ||
            (terrain.attachedRigidbody && terrain.attachedRigidbody.bodyType != RigidbodyType2D.Static) ||
            Mathf.Abs(Mathf.DeltaAngle(terrain.transform.eulerAngles.z, 0)) > 0.01f) return false;
        var bounds = terrain.bounds;
        if (bounds.size.y > flipProbeDistance) return false;
        var dogBounds = shape.bounds;
        bool inverted = Mode != SurfaceMode.Inverted;
        Vector2 center = dogBounds.center;
        center.y = inverted ? bounds.min.y - dogBounds.extents.y - surfaceOffset
                            : bounds.max.y + dogBounds.extents.y + surfaceOffset;
        // Check all solid colliding layers, not just Ground. Never ignore the source terrain.
        if (!HasRoom(center, dogBounds.size)) return false;
        Vector2 rotatedOffset = -((Vector2)dogBounds.center - body.position);
        body.position = center - rotatedOffset;
        body.rotation = inverted ? 180f : 0f;
        Mode = inverted ? SurfaceMode.Inverted : SurfaceMode.Normal;
        body.linearVelocity = new Vector2(body.linearVelocity.x, 0);
        ApplyGravity();
        nextFlipTime = Time.time + flipCooldown;
        return true;
    }

    private bool HasRoom(Vector2 center, Vector2 size)
    {
        var filter = new ContactFilter2D();
        filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
        filter.useTriggers = false;
        // Allocating here avoids a truncated overlap buffer accepting an occupied destination.
        var overlaps = new System.Collections.Generic.List<Collider2D>();
        Physics2D.OverlapBox(center, size, 0, filter, overlaps);
        foreach (var other in overlaps)
            if (other.attachedRigidbody != body && !Physics2D.GetIgnoreCollision(shape, other)) return false;
        return true;
    }

    private void Release()
    {
        // No repositioning: centered collider stays in place while normal gravity resumes.
        Mode = SurfaceMode.Released;
        //body.rotation = 0;
        body.linearVelocity =
            new Vector2(body.linearVelocity.x, Mathf.Min(-0.5f, body.linearVelocity.y));
        //body.linearVelocity = new Vector2(body.linearVelocity.x, Mathf.Min(0, body.linearVelocity.y));
        attachedSurface = null;
        ApplyGravity();
    }

    private void ApplyGravity() => body.gravityScale = Mode == SurfaceMode.Inverted ? -gravityScale : gravityScale;

    private void LateUpdate()
    {
        if (Mathf.Abs(body.linearVelocity.x) > 0.01f) facingRight = body.linearVelocity.x > 0;
        bool visuallyUpsideDown =
            Mode == SurfaceMode.Inverted ||
            (Mode == SurfaceMode.Released && Mathf.Abs(Mathf.DeltaAngle(body.rotation, 180f)) < 1f);
        
        if (visual)
        {
            visual.flipX =
                (facingRight != spriteFacesRight) ^ visuallyUpsideDown;
        }//visual.flipX = (facingRight != spriteFacesRight) ^ (Mode == SurfaceMode.Inverted);

        visual.flipX =
            (facingRight != spriteFacesRight) ^ visuallyUpsideDown;
        if (!animator) return;
        if (hasSpeed) animator.SetFloat(speedParameter, Mathf.Abs(body.linearVelocity.x));
        if (hasGrounded) animator.SetBool(groundedParameter, IsGrounded);
        if (!playExistingStates) return;
        int state = Animator.StringToHash(!IsGrounded ? jumpState : Mathf.Abs(body.linearVelocity.x) > 0.01f ? runState : idleState);
        if (state != playingState && animator.HasState(0, state))
        {
            animator.Play(state, 0, 0);
            playingState = state;
        }
    }

    private void OnDisable() { horizontal = 0; jumpRequested = flipRequested = releaseRequested = false; }

    private void OnDrawGizmosSelected()
    {
        var collider = GetComponent<BoxCollider2D>();
        if (!collider) return;
        Gizmos.color = IsGrounded ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(collider.bounds.center + (Vector3)(GravityDirection * groundCheckDistance), collider.bounds.size);
    }
}
