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
    [Header("Jump response")]
    [SerializeField, Min(0)] private float coyoteTime = 0.08f;
    [SerializeField, Min(0)] private float jumpBufferTime = 0.1f;
    [Header("Survival")]
    [SerializeField, Min(0.1f)] private float maxSafeFallDistance = 6f;
    [Tooltip("Viewport fraction below the bottom edge. Horizontal edges never kill.")]
    [SerializeField, Min(0)] private float outOfViewMargin = 0.2f;
    [SerializeField, Min(0)] private float safePositionDelay = 0.15f;
    [SerializeField, Min(0)] private float respawnDelay = 0.2f;
    [SerializeField, Min(0)] private float respawnGraceTime = 0.35f;
    [SerializeField] private Camera gameplayCamera;
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
    public bool GameplayLocked => lifeState != LifeState.Playing;
    private enum LifeState { Playing, Respawning, Complete }
    private LifeState lifeState;
    private Vector2 levelStart, lastSafePosition, respawnDestination;
    private float groundedUntil = float.NegativeInfinity, jumpUntil = float.NegativeInfinity;
    private float stableSince, fallPeak, respawnAt, protectedUntil;
    private bool trackingFall;
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
        levelStart = lastSafePosition = body.position;
        if (!gameplayCamera) gameplayCamera = Camera.main;
        stableSince = Time.time;
        if (animator)
            foreach (var parameter in animator.parameters)
            {
                hasSpeed |= parameter.name == speedParameter && parameter.type == AnimatorControllerParameterType.Float;
                hasGrounded |= parameter.name == groundedParameter && parameter.type == AnimatorControllerParameterType.Bool;
            }
    }

    private void Update()
    {
        if (GameplayLocked) return;
        horizontal = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0)
                   - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
        jumpRequested |= Input.GetKeyDown(jumpKey);
        flipRequested |= Input.GetKeyDown(flipKey);
        releaseRequested |= Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
    }

    private void FixedUpdate()
    {
        if (lifeState == LifeState.Respawning)
        {
            if (Time.time >= respawnAt) FinishRespawn();
            return;
        }
        if (GameplayLocked) return;
        DetectSurface();
        UpdateSurvival();
        if (GameplayLocked) return;
        if (IsGrounded) groundedUntil = Time.time + coyoteTime;
        if (jumpRequested) jumpUntil = Time.time + jumpBufferTime;
        body.linearVelocity = new Vector2(horizontal * moveSpeed, body.linearVelocity.y);
        if (releaseRequested && Mode == SurfaceMode.Inverted) Release();
        else if (flipRequested && TryFlip()) { }
        else if (Time.time <= jumpUntil && Time.time <= groundedUntil && Mode != SurfaceMode.Released)
        {
            // Away from the supporting surface, in either orientation.
            body.linearVelocity = new Vector2(body.linearVelocity.x, -GravityDirection.y * jumpForce);
            attachedSurface = null;
            groundedUntil = jumpUntil = float.NegativeInfinity;
            stableSince = Time.time;
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
            if (IsSpecialSurface(hit.collider)) continue;
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
            Vector2 center = shape.bounds.center;
            body.rotation = 0f;
            // The artist-adjusted collider has an offset; preserve its world center on rotation.
            body.position = center - Vector2.Scale(shape.offset, transform.lossyScale);

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
        ResetFallAndJump();
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
        ResetFallAndJump();
        trackingFall = true;
        fallPeak = shape.bounds.center.y;
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

    private static bool IsSpecialSurface(Collider2D other) =>
        other.GetComponentInParent<Trap2D>() || other.GetComponentInParent<LevelGoal2D>();

    private void OnCollisionEnter2D(Collision2D collision) => HandleCollision(collision);
    private void OnCollisionStay2D(Collision2D collision) => HandleCollision(collision);
    private void HandleCollision(Collision2D collision)
    {
        if (GameplayLocked || Vector2.Dot(body.linearVelocity, GravityDirection) >= 0) return;
        foreach (var contact in collision.contacts)
            if (Mathf.Abs(contact.normal.x) > 0.8f)
            {
                // A side wall ends ascent in BOTH gravity orientations, without moving the body.
                body.linearVelocity = new Vector2(body.linearVelocity.x, 0);
                groundedUntil = float.NegativeInfinity;
                break;
            }
    }

    private void UpdateSurvival()
    {
        if (Mode == SurfaceMode.Inverted) trackingFall = false;
        else
        {
            // Track the apex while airborne, not jump ascent or a Flip's teleport distance.
            if (!IsGrounded && !trackingFall) { fallPeak = shape.bounds.center.y; trackingFall = true; }
            if (trackingFall)
            {
                fallPeak = Mathf.Max(fallPeak, shape.bounds.center.y);
                if (fallPeak - shape.bounds.center.y > maxSafeFallDistance) { Die(false); return; }
                if (IsGrounded) trackingFall = false;
            }
            if (!IsGrounded && body.linearVelocity.y < -0.1f && gameplayCamera &&
                gameplayCamera.WorldToViewportPoint(shape.bounds.max).y < -outOfViewMargin)
            { Die(false); return; }
        }
        // Only stationary, supported NORMAL positions become checkpoints. Include triggers in safety checks.
        if (Mode != SurfaceMode.Normal || !IsGrounded || Mathf.Abs(body.linearVelocity.x) > 0.1f ||
            Mathf.Abs(horizontal) > 0.01f || Time.time < nextFlipTime || !SafeSpawn(body.position, out _))
            stableSince = Time.time;
        else if (Time.time - stableSince >= safePositionDelay)
            SafeSpawn(body.position, out lastSafePosition);
    }

    private bool SafeSpawn(Vector2 point, out Vector2 clearPoint)
    {
        Vector2 size = Vector2.Scale(shape.size, new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)));
        Vector2 offset = Vector2.Scale(shape.offset, transform.lossyScale);
        // Small upward clearance also removes contact slop from a recorded resting position.
        for (int i = 0; i < 5; i++)
        {
            clearPoint = point + Vector2.up * (surfaceOffset + i * 0.025f);
            bool blocked = false;
            var filter = new ContactFilter2D { useTriggers = true };
            var overlaps = new System.Collections.Generic.List<Collider2D>();
            Physics2D.OverlapBox(clearPoint + offset, size, 0, filter, overlaps);
            foreach (var other in overlaps)
                if (other.attachedRigidbody != body &&
                    (IsSpecialSurface(other) || (!other.isTrigger &&
                     !Physics2D.GetIgnoreLayerCollision(gameObject.layer, other.gameObject.layer) &&
                     !Physics2D.GetIgnoreCollision(shape, other)))) { blocked = true; break; }
            if (!blocked) return true;
        }
        clearPoint = point;
        return false;
    }

    public void Die(bool trapDeath)
    {
        if (GameplayLocked || Time.time < protectedUntil) return;
        respawnDestination = trapDeath ? levelStart : lastSafePosition;
        lifeState = LifeState.Respawning;
        respawnAt = Time.time + respawnDelay;
        body.linearVelocity = Vector2.zero;
        body.simulated = false;
        ClearInputs();
    }

    private void FinishRespawn()
    {
        if (!SafeSpawn(respawnDestination, out var position) && !SafeSpawn(levelStart, out position))
        {
            // A changed level can block both checkpoints. Wait safely, never embed the Dog.
            respawnAt = Time.time + 0.25f;
            return;
        }
        // While simulation is disabled, synchronize the Transform too, otherwise interpolation
        // can restore the old physics pose when the body is re-enabled.
        transform.SetPositionAndRotation(new Vector3(position.x, position.y, transform.position.z), Quaternion.identity);
        body.position = position;
        body.rotation = 0;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0;
        Mode = SurfaceMode.Normal;
        attachedSurface = null;
        ResetFallAndJump();
        ClearInputs();
        ApplyGravity();
        nextFlipTime = Time.time + flipCooldown;
        protectedUntil = Time.time + respawnGraceTime;
        lifeState = LifeState.Playing;
        body.simulated = true;
        Physics2D.SyncTransforms();
        if (gameplayCamera && gameplayCamera.TryGetComponent<DeadZoneCamera2D>(out var follow)) follow.ResetAfterRespawn(position);
    }

    public void CompleteLevel()
    {
        if (GameplayLocked) return;
        lifeState = LifeState.Complete;
        body.linearVelocity = Vector2.zero;
        body.simulated = false;
        ClearInputs();
    }

    private void ClearInputs() { horizontal = 0; jumpRequested = flipRequested = releaseRequested = false; jumpUntil = float.NegativeInfinity; }
    private void ResetFallAndJump()
    {
        trackingFall = false;
        groundedUntil = jumpUntil = float.NegativeInfinity;
        stableSince = Time.time;
    }

    private void OnDrawGizmosSelected()
    {
        var collider = GetComponent<BoxCollider2D>();
        if (!collider) return;
        Gizmos.color = IsGrounded ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(collider.bounds.center + (Vector3)(GravityDirection * groundCheckDistance), collider.bounds.size);
    }
}
