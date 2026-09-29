using UnityEngine;

[DisallowMultipleComponent]
public sealed class Trap2D : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other) => Touch(other);
    private void OnTriggerStay2D(Collider2D other) => Touch(other);
    private void OnCollisionEnter2D(Collision2D other) => Touch(other.collider);
    private void OnCollisionStay2D(Collision2D other) => Touch(other.collider);
    private void Touch(Collider2D other)
    {
        var dog = other.GetComponentInParent<DogController2D>();
        if (dog) dog.Die(true);
    }
}
