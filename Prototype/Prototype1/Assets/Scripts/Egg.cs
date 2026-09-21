using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class Egg : MonoBehaviour
{
    public enum EggState { Intact, Broken, Destroyed }
    [SerializeField] private Sprite brokenSprite;
    [SerializeField] private EggState state;
    public EggState State => state;
    public bool IsBroken => state == EggState.Broken;
    public bool IsDestroyed => state == EggState.Destroyed;
    GameManager gameManager;
    SpriteRenderer visual;

    void Awake()
    {
        visual = GetComponent<SpriteRenderer>();
        
        if (!GetComponent<Collider2D>()) gameObject.AddComponent<PolygonCollider2D>();
    }

    public void Initialize(GameManager manager)
    {
        gameManager = manager;
        if (IsDestroyed) HideDestroyedEgg();
        else if (IsBroken && brokenSprite) visual.sprite = brokenSprite;
    }

    public bool TryTakeDamage()
    {
        if (IsDestroyed || !gameManager || !gameManager.CanDamageEgg(this)) return false;
        EggState previous = state;
        if (state == EggState.Intact)
        {
            state = EggState.Broken;
            if (brokenSprite) visual.sprite = brokenSprite;
            else Debug.LogWarning("Broken sprite is not assigned.", this);
        }
        else
        {
            state = EggState.Destroyed;
            HideDestroyedEgg();
        }
        gameManager.RecordEggDamage(this, previous);
        return true;
    }

    void HideDestroyedEgg()
    {
        // Egg is itself the parent of the other eggs in the inspected hierarchy.
        // Disable only this egg's components, never its child eggs or GameObject.
        visual.enabled = false;
        foreach (Collider2D collider in GetComponents<Collider2D>()) collider.enabled = false;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (IsDestroyed) return;
        StraightLineMouse mouse = other.GetComponentInParent<StraightLineMouse>();
        if (mouse) mouse.TryHitEgg(this);
    }
}
