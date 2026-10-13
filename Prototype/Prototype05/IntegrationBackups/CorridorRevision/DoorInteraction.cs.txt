using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DoorInteraction : MonoBehaviour
{
    public Transform hinge;
    public Vector3 hingeAxis = Vector3.up;
    public float openAngle = 100;
    [Min(0.01f)] public float openDuration = 0.55f;
    public GameObject blackVoid;
    [Tooltip("For a panel that is already open in the scene; preserves its existing pose.")]
    public bool startsOpen;
    [Tooltip("Disable only the moving panel's colliders once fully open if the authored animation leaves the passage blocked.")]
    public bool releasePanelCollidersWhenOpen;
    [Tooltip("Decorative collider planes verified to cross this doorway. Ignored only against the player after opening; meshes and other collisions remain unchanged.")]
    public Collider[] passageObstacles = new Collider[0];
    [Header("Optional existing Animator")]
    public Animator animator;
    public string openingState;
    public string openingParameter;
    public bool HasBeenOpened { get; private set; }
    public bool IsOpen { get; private set; }
    public Quaternion ClosedRotation { get; private set; }
    public bool CanInteract => isActiveAndEnabled && !HasBeenOpened && hinge && blackVoid;
    bool oldAnimatorEnabled;
    float oldAnimatorSpeed;
    struct CollisionPair { public Collider player, obstacle; public bool previouslyIgnored; }
    readonly List<CollisionPair> ignoredPairs = new List<CollisionPair>();
    void Awake()
    {
        if (!hinge) hinge = transform;
        ClosedRotation = hinge.localRotation;
        HasBeenOpened = IsOpen = startsOpen;
        if (blackVoid) blackVoid.SetActive(startsOpen);
        if (animator)
        { oldAnimatorEnabled = animator.enabled; oldAnimatorSpeed = animator.speed; animator.enabled = false; }
        if (startsOpen) ReleasePassage();
    }
    public bool TryOpen()
    {
        if (!Application.isPlaying || HasBeenOpened || !hinge || !blackVoid) return false;
        HasBeenOpened = true;
        // Mask is enabled before any gap appears in the moving door.
        blackVoid.SetActive(true);
        StartCoroutine(Open());
        return true;
    }
    IEnumerator Open()
    {
        bool animated = animator && animator.runtimeAnimatorController;
        if (animated)
        {
            animator.enabled = true; animator.speed = 1;
            if (!string.IsNullOrEmpty(openingState) && animator.HasState(0, Animator.StringToHash(openingState)))
                animator.Play(openingState, 0, 0);
            else
            {
                var parameter = animator.parameters.FirstOrDefault(p => p.name == openingParameter);
                if (parameter != null && parameter.type == AnimatorControllerParameterType.Trigger) animator.SetTrigger(openingParameter);
                else if (parameter != null && parameter.type == AnimatorControllerParameterType.Bool) animator.SetBool(openingParameter, true);
                else { animated = false; animator.enabled = false; }
            }
        }
        if (animated)
        {
            yield return new WaitForSeconds(openDuration);
            // Imported opening clips may loop. Hold their last open pose.
            if (!string.IsNullOrEmpty(openingState)) { animator.Play(openingState, 0, 0.9999f); animator.Update(0); }
            animator.speed = 0;
        }
        else
        {
            var target = ClosedRotation * Quaternion.AngleAxis(openAngle, hingeAxis.normalized);
            for (float elapsed = 0; elapsed < openDuration; elapsed += Time.deltaTime)
            { hinge.localRotation = Quaternion.Slerp(ClosedRotation, target, Mathf.SmoothStep(0, 1, elapsed / openDuration)); yield return null; }
            hinge.localRotation = target;
        }
        IsOpen = true;
        ReleasePassage();
    }
    void ReleasePassage()
    {
        if (releasePanelCollidersWhenOpen)
            foreach (var collider in hinge.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        var manager = FindFirstObjectByType<CorridorManager>();
        if (!manager || !manager.loop || !manager.loop.player) return;
        foreach (var playerCollider in manager.loop.player.GetComponentsInChildren<Collider>())
        foreach (var obstacle in passageObstacles)
        {
            if (!obstacle || !obstacle.enabled || !obstacle.gameObject.activeInHierarchy) continue;
            ignoredPairs.Add(new CollisionPair { player = playerCollider, obstacle = obstacle,
                previouslyIgnored = Physics.GetIgnoreCollision(playerCollider, obstacle) });
            Physics.IgnoreCollision(playerCollider, obstacle, true);
        }
    }
    void LateUpdate()
    {
        // Unity may recreate a CharacterController during teleportation.
        foreach (var pair in ignoredPairs)
            if (pair.player && pair.player.enabled && pair.obstacle && pair.obstacle.enabled
                && !Physics.GetIgnoreCollision(pair.player, pair.obstacle)) Physics.IgnoreCollision(pair.player, pair.obstacle, true);
    }
    void OnDisable()
    {
        foreach (var pair in ignoredPairs) if (pair.player && pair.obstacle) Physics.IgnoreCollision(pair.player, pair.obstacle, pair.previouslyIgnored);
        ignoredPairs.Clear();
        if (animator) { animator.speed = oldAnimatorSpeed; animator.enabled = oldAnimatorEnabled; }
    }
}
