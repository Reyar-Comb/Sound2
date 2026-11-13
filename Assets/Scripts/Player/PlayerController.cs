using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerController : MonoBehaviour
{
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference ascendAction;
    [SerializeField] private InputActionReference descendAction;
    [SerializeField] private Transform cameraTransform;
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f)] private float ascendSpeed = 3f;
    [SerializeField, Min(0f)] private float descendSpeed = 3f;
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.1f;
    [SerializeField, Range(0f, 89f)] private float maxPitch = 89f;
    [SerializeField] private bool invertY;

    private CharacterController characterController;
    private float yaw;
    private float pitch;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        characterController.stepOffset = 0f;
        characterController.minMoveDistance = 0f;

        if (moveAction == null || moveAction.action == null
            || lookAction == null || lookAction.action == null
            || ascendAction == null || ascendAction.action == null
            || descendAction == null || descendAction.action == null || cameraTransform == null)
        {
            Debug.LogError("Assign Move, Look, Ascend, Descend and Camera Transform references.", this);
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
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && isActiveAndEnabled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        Vector2 look = lookAction.action.ReadValue<Vector2>() * mouseSensitivity;
        yaw = Mathf.Repeat(yaw + look.x, 360f);
        pitch = Mathf.Clamp(pitch + (invertY ? look.y : -look.y), -maxPitch, maxPitch);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        Vector2 move = Vector2.ClampMagnitude(moveAction.action.ReadValue<Vector2>(), 1f);
        Vector3 direction = transform.right * move.x + transform.forward * move.y;
        int verticalInput = (ascendAction.action.IsPressed() ? 1 : 0)
            - (descendAction.action.IsPressed() ? 1 : 0);
        float verticalSpeed = verticalInput > 0 ? ascendSpeed : descendSpeed;
        Vector3 velocity = direction * moveSpeed + Vector3.up * (verticalInput * verticalSpeed);
        characterController.Move(velocity * Time.deltaTime);
    }
}
