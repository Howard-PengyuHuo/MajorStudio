using UnityEngine;

public sealed class StraightLineMouse : MonoBehaviour
{
    MouseSpawner owner;
    Vector2 moveDirection;
    float speed;
    float radius;
    bool stopped;
    Collider2D mouseCollider;
    SpriteRenderer visual;
    bool hasHitEgg;
    public bool IsAlive { get; private set; }

    public float AttackHitRadius
    {
        get
        {
            if (!mouseCollider || !mouseCollider.enabled) return 0f;
            Vector3 extents = mouseCollider.bounds.extents;
            float bodyRadius = Mathf.Min(extents.x, extents.y);
            // The existing circular collider encloses the entire artwork and tail.
            // Cap contact allowance at the sprite's narrow half-size, independent
            // of its diagonal rotation, so that broad path-clearance radius does
            // not turn into an excessively generous attack hitbox.
            if (visual && visual.sprite)
            {
                Vector3 spriteExtents = visual.sprite.bounds.extents;
                Vector3 scale = visual.transform.lossyScale;
                bodyRadius = Mathf.Min(bodyRadius, Mathf.Abs(spriteExtents.x * scale.x), Mathf.Abs(spriteExtents.y * scale.y));
            }
            return Mathf.Max(0f, bodyRadius);
        }
    }

    public void Initialize(MouseSpawner spawner, Vector2 direction, float moveSpeed, float pathRadius)
    {
        owner = spawner;
        moveDirection = direction.normalized;
        speed = moveSpeed;
        radius = pathRadius;
        IsAlive = true;
        hasHitEgg = false;
        visual = GetComponent<SpriteRenderer>();
        
        CircleCollider2D collider = GetComponent<CircleCollider2D>();
        if (!collider) collider = gameObject.AddComponent<CircleCollider2D>();
        collider.radius = radius / Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        collider.isTrigger = true;
        mouseCollider = collider;
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (!body) body = gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
    }

    void FixedUpdate()
    {
        if (!IsAlive || stopped || !owner || !owner.GameplayActive) return;
        float distance = speed * Time.fixedDeltaTime;
       
        Egg egg = owner.FindEggHit(transform.position, moveDirection, distance, radius);
        if (egg && TryHitEgg(egg)) return;
        transform.position += (Vector3)(moveDirection * distance);
    }

    public bool TryHitEgg(Egg egg)
    {
        if (!IsAlive || stopped || hasHitEgg || !owner || !owner.GameplayActive || !egg) return false;
        // Both the swept contact and trigger callback use this one guarded path.
        hasHitEgg = true;
        if (!egg.TryTakeDamage()) { hasHitEgg = false; return false; }
        Kill();
        return true;
    }

    public void Kill()
    {
        if (!IsAlive) return;
        IsAlive = false;
        stopped = true;
        if (mouseCollider) mouseCollider.enabled = false;
        if (owner) owner.Forget(this);
        Destroy(gameObject);
    }
    public void Stop() => stopped = true;
    void OnDestroy() { if (owner) owner.Forget(this); }
}
