using UnityEngine;

public class FpsControllerSimple : MonoBehaviour
{
    public float speed = 6.0f;
    public float sensitivity = 2.0f;
    public float gravity = 20.0f; // Additional gravity

    private CharacterController characterController;
    private Camera playerCamera;

    private float moveFB;
    private float moveLR;
    private float rotX;
    private float rotY;
    private Vector3 moveDirection = Vector3.zero; // Movement direction

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>();

        // Lock and hide the cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // Player movement
        moveFB = Input.GetAxis("Vertical") * speed;
        moveLR = Input.GetAxis("Horizontal") * speed;

        Vector3 movement = new Vector3(moveLR, 0, moveFB);
        movement = transform.rotation * movement;

        if (characterController.isGrounded) // If the player is grounded
        {
            moveDirection = movement;
            moveDirection *= speed;
        }

        moveDirection.y -= gravity * Time.deltaTime; // Apply gravity
        characterController.Move(moveDirection * Time.deltaTime);

        // Player rotation with the mouse
        rotX = Input.GetAxis("Mouse X") * sensitivity;
        rotY = Input.GetAxis("Mouse Y") * sensitivity;

        transform.Rotate(0, rotX, 0);
        playerCamera.transform.Rotate(-rotY, 0, 0);
    }
}
