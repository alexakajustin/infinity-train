using UnityEngine;

public class FirstPersonController : MonoBehaviour
{
    [Header("Movement Settings")]
    public bool canMove = true;
    public bool canLook = true;
    public bool canSprint = true; // Enable/disable sprinting
    public KeyCode sprintKey = KeyCode.LeftShift; // Key to trigger sprint

    [Header("References")]
    public Camera playerCamera;
    public Transform playerHead;
    public CharacterController characterController;
    public Transform groundCheckPoint;

    [Header("Movement Parameters")]
    public float moveSpeed = 10f;
    public float sprintMultiplier = 1.5f; // 50% faster when sprinting
    public float lookSpeed = 200f;
    public float jumpForce = 5f;
    public float groundCheckRadius = 0.2f;
    public float gravity = -19.62f;
    public float slopeLimit = 45f;
    public float slideFriction = 0.1f;

    private float xRotation = 0f;
    private Vector3 velocity;
    private bool isGrounded;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        if (characterController != null)
            characterController.slopeLimit = slopeLimit;
    }

    private void Update()
    {
        if (ChunkManager.Instance.hasFirstChunkGenerated == false) return; // Prevent movement before the first chunk is generated
        if (canMove)
            HandleMovement();
        if (canLook)
            HandleLook();

        if(Input.GetKeyDown(KeyCode.Escape))
        {
            // Show cursor  
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = !Cursor.visible;
        }
    }

    private void HandleMovement()
    {
        // Ground check
        int layerMask = ~LayerMask.GetMask("Ignore Raycast", "Player");
        isGrounded = Physics.CheckSphere(groundCheckPoint.position, groundCheckRadius, layerMask);

        // Get input
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        Vector3 moveDirection = new Vector3(moveX, 0f, moveZ).normalized;

        // Calculate movement
        Vector3 moveDir = Vector3.zero;
        if (moveDirection.magnitude >= 0.1f)
        {
            float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg + playerCamera.transform.eulerAngles.y;
            float currentSpeed = moveSpeed;
            if (canSprint && Input.GetKey(sprintKey))
                currentSpeed *= sprintMultiplier; // Apply sprint speed
            moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward * currentSpeed;
        }

        // Apply gravity
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = gravity;
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }

        // Handle jump
        if (isGrounded && Input.GetButtonDown("Jump"))
        {
            velocity.y = Mathf.Sqrt(jumpForce * -2f * gravity);
        }

        // Prevent sliding on slopes
        if (isGrounded && moveDirection.magnitude < 0.1f) // No input
        {
            RaycastHit hit;
            if (Physics.Raycast(groundCheckPoint.position, Vector3.down, out hit, groundCheckRadius + 0.1f, layerMask))
            {
                float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
                if (slopeAngle > slopeLimit)
                {
                    moveDir = Vector3.ProjectOnPlane(moveDir, hit.normal).normalized * moveDir.magnitude;
                    moveDir *= slideFriction;
                }
                else
                {
                    moveDir = Vector3.zero;
                }
            }
            else
            {
                moveDir = Vector3.zero;
            }
        }

        // Combine movement and vertical velocity
        Vector3 finalMove = (moveDir + velocity) * Time.deltaTime;
        characterController.Move(finalMove);
    }

    private void HandleLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * lookSpeed * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * lookSpeed * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -70f, 55f);

        playerHead.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheckPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheckPoint.position, groundCheckRadius);
        }
    }
}