using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PlayerMovement : MonoBehaviour
{
    public InputActionAsset inputActions;
    [Min(0f)] public float movementSpeed = 4f;
    [SerializeField] SpriteRenderer spriteRenderer;
    Rigidbody2D body;
    InputActionAsset runtimeActions;
    InputAction move;
    Vector2 movement;

    void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        if (spriteRenderer == null) Debug.LogWarning("PlayerMovement: Assign the Player SpriteRenderer to enable horizontal facing.", this);
        body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        if (inputActions == null) {
            Debug.LogError("PlayerMovement: Assign the project's InputSystem_Actions asset.", this);
            enabled = false;
            return;
        }
        // Each player owns its enabled action state, without altering the source asset.
        runtimeActions = Instantiate(inputActions);
        move = runtimeActions.FindAction("Player/Move");
        if (move == null) {
            Debug.LogError("PlayerMovement: Input actions need a Player/Move Vector2 action.", this);
            enabled = false;
        }
    }

    void OnEnable() { move?.Enable(); }

    void Update()
    {
        movement = move == null ? Vector2.zero : Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
        if (spriteRenderer != null && movement.x != 0f) spriteRenderer.flipX = movement.x < 0f;
    }

    void FixedUpdate()
    {
        body.linearVelocity = movement * movementSpeed;
    }

    void OnDisable()
    {
        move?.Disable();
        movement = Vector2.zero;
        if (body != null) body.linearVelocity = Vector2.zero;
    }

    void OnDestroy()
    {
        if (runtimeActions != null) Destroy(runtimeActions);
    }
}
