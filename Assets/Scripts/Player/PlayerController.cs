using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerController : MonoBehaviour
{
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference verticalAction;
    [SerializeField] private Transform cameraTransform;
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f)] private float ascendSpeed = 3f;
    [SerializeField, Min(0f)] private float descendSpeed = 3f;
    [SerializeField, Range(0f, 89f)] private float maxPitch = 89f;
    [SerializeField] private bool useGravity;
    [SerializeField, Min(0f)] private float gravity = 20f;
    [SerializeField, Min(0f)] private float terminalFallSpeed = 40f;

    private CharacterController characterController;
    private float yaw;
    private float pitch;
    private float fallSpeed;
    public bool InputSuspended { get; set; }
    public Transform ViewTransform => cameraTransform;
    public Quaternion AimLocalRotation => Quaternion.Euler(pitch, 0f, 0f);
    public bool UsesGravity => useGravity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (moveAction == null || moveAction.action == null
            || lookAction == null || lookAction.action == null
            || verticalAction == null || verticalAction.action == null || cameraTransform == null)
        {
            Debug.LogError("Assign Move, Look, Vertical and Camera Transform references.", this);
            enabled = false;
            return;
        }

        yaw = transform.eulerAngles.y;
        pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, cameraTransform.localEulerAngles.x), -maxPitch, maxPitch);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void OnEnable()
    {
        if (InputSuspended) return;
        ApplyCursorCapture(true);
    }

    private void OnDisable()
    {
        ApplyCursorCapture(false);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && isActiveAndEnabled && !InputSuspended)
        {
            ApplyCursorCapture(true);
        }
    }

    public void SetInputCaptured(bool captured)
    {
        InputSuspended = !captured;
        ApplyCursorCapture(captured && isActiveAndEnabled);
    }

    private static void ApplyCursorCapture(bool captured)
    {
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }

    private void Update()
    {
        if (InputSuspended) return;
        Vector2 look = lookAction.action.ReadValue<Vector2>();
        yaw = Mathf.Repeat(yaw + look.x, 360f);
        pitch = Mathf.Clamp(pitch + look.y, -maxPitch, maxPitch);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        Vector2 move = Vector2.ClampMagnitude(moveAction.action.ReadValue<Vector2>(), 1f);
        Vector3 direction = transform.right * move.x + transform.forward * move.y;
        float verticalInput = verticalAction.action.ReadValue<float>();
        float verticalSpeed = verticalInput > 0 ? ascendSpeed : descendSpeed;
        Vector3 velocity = direction * moveSpeed + Vector3.up * (verticalInput * verticalSpeed);
        if (useGravity)
        {
            if (characterController.isGrounded && fallSpeed < 0) fallSpeed = -2f;
            fallSpeed = Mathf.Max(fallSpeed - gravity * Time.deltaTime, -terminalFallSpeed);
            velocity.y = fallSpeed;
        }
        var collisions = characterController.Move(velocity * Time.deltaTime);
        if (useGravity && (collisions & CollisionFlags.Above) != 0) fallSpeed = Mathf.Min(fallSpeed, 0);
    }
}
