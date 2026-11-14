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
    [SerializeField] private bool useGravity = true;
    [SerializeField, Min(0f)] private float gravity = 20f;
    [SerializeField, Min(0f)] private float terminalFallSpeed = 40f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
    [SerializeField, Min(0f), Tooltip("Ignore tiny ground-contact changes when reporting a landing.")]
    private float minimumLandingSpeed = 2.5f;

    private CharacterController characterController;
    private float yaw;
    private float pitch;
    private float fallSpeed;
    private float airborneTime;
    private InputAction jumpAction;
    private Vector3 pushVelocity;
    public event System.Action<float> Landed;
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

        jumpAction = moveAction.action.actionMap.FindAction("Jump");
        if (jumpAction == null)
        {
            Debug.LogError("The player input map must contain the Jump action.", this);
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
        PropFragments.ExcludeFromPlayer(this);
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
        if(SonicSpaceStartMenu.BlocksGameplay)captured=false;
        InputSuspended = !captured;
        ApplyCursorCapture(captured && isActiveAndEnabled);
    }

    public void Teleport(Vector3 position)
    {
        bool wasEnabled = characterController.enabled;
        characterController.enabled = false;
        transform.position = position;
        fallSpeed = 0;
        airborneTime = 0;
        characterController.enabled = wasEnabled;
        Physics.SyncTransforms();
    }

    private static void ApplyCursorCapture(bool captured)
    {
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }

    private void Update()
    {
        if (InputSuspended || !characterController.enabled) return;
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
        bool wasGrounded = characterController.isGrounded;
        if (useGravity)
        {
            if (wasGrounded)
            {
                if (fallSpeed < 0) fallSpeed = -2f;
                if (jumpAction.WasPressedThisFrame() && gravity > 0 && jumpHeight > 0)
                    fallSpeed = Mathf.Sqrt(2 * gravity * jumpHeight);
            }
            fallSpeed = Mathf.Max(fallSpeed - gravity * Time.deltaTime, -terminalFallSpeed);
            velocity.y = fallSpeed;
        }
        pushVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        var collisions = characterController.Move(velocity * Time.deltaTime);
        pushVelocity = Vector3.zero;
        if (useGravity)
        {
            bool grounded = (collisions & CollisionFlags.Below) != 0;
            if (grounded)
            {
                float impactSpeed = Mathf.Max(0, -velocity.y);
                if (!wasGrounded && airborneTime >= .08f && impactSpeed >= minimumLandingSpeed)
                    Landed?.Invoke(impactSpeed);
                airborneTime = 0;
                fallSpeed = -2f;
            }
            else airborneTime += Time.deltaTime;
            if ((collisions & CollisionFlags.Above) != 0) fallSpeed = Mathf.Min(fallSpeed, 0);
        }
        else airborneTime = 0;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!isActiveAndEnabled || InputSuspended || SonicSpaceStartMenu.BlocksGameplay) return;
        var body = hit.collider.attachedRigidbody;
        if (body == null || !body.TryGetComponent<PushableProp>(out var prop)) return;
        prop.RequestPush(pushVelocity, hit.point, hit.normal);
    }
}
