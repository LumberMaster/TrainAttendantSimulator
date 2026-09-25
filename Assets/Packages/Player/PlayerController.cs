using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Ссылки")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private Animator animator;

    [Header("Input Actions (перетащи сюда из .inputactions)")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference sprintAction;
    [SerializeField] private InputActionReference crouchAction;
    [SerializeField] private InputActionReference attackAction;

    [Header("Движение")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float sprintSpeed = 6f;
    [SerializeField] private float crouchSpeed = 1.5f;
    [SerializeField] private float moveSmoothTime = 0.12f;

    [Header("Прыжок и гравитация")]
    [SerializeField] private float jumpHeight = 1.1f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundCheckRadius = 0.15f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Приседание")]
    [SerializeField] private float standHeight = 1.8f;
    [SerializeField] private float crouchHeight = 1.0f;
    [SerializeField] private float crouchSmooth = 8f;

    [Header("Камера")]
    [SerializeField] private float lookSensitivity = 0.15f;
    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;
    [SerializeField] private float lookSmoothTime = 0.05f;

    [Header("Курсор")]
    [Tooltip("Скрывать курсор при старте игры")]
    [SerializeField] private bool hideCursorOnStart = true;

    [Header("Анимации")]
    [SerializeField] private string animSpeed = "Speed";
    [SerializeField] private string animGrounded = "Grounded";
    [SerializeField] private string animCrouch = "Crouch";
    [SerializeField] private string animJumpTrigger = "Jump";
    [SerializeField] private string animAttackTrigger = "Attack";

    [Header("UnityEvents")]
    public UnityEvent OnJump;
    public UnityEvent OnLand;
    public UnityEvent OnStartSprint;
    public UnityEvent OnStopSprint;
    public UnityEvent OnStartCrouch;
    public UnityEvent OnStopCrouch;
    public UnityEvent OnAttack;
    public UnityEvent OnStartMove;
    public UnityEvent OnStopMove;

    private CharacterController controller;
    private Vector2 moveInput;

    private Vector3 smoothMoveVelocity;
    private Vector3 currentMoveVelocity;
    private float verticalVelocity;
    private bool isGrounded, wasGrounded, isSprinting, isCrouching, isMoving;

    private float currentYaw, targetYaw, currentPitch, targetPitch, yawVelocity, pitchVelocity;

    // Блокировка ввода (используется при показе курсора — пауза, инвентарь и т.п.)
    private bool inputLocked;

    // =====================================================================
    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraRoot == null && Camera.main != null) cameraRoot = Camera.main.transform;

        currentYaw = targetYaw = transform.eulerAngles.y;
        currentPitch = targetPitch = 0f;

        controller.height = standHeight;
        controller.center = new Vector3(0f, standHeight * 0.5f, 0f);
    }

    private void Start()
    {
        // Применяем стартовое состояние курсора (скрыт = играем)
        SetCursorVisible(!hideCursorOnStart);
    }

    private void OnEnable()
    {
        if (moveAction != null)
        {
            moveAction.action.performed += OnMovePerformed;
            moveAction.action.canceled += OnMoveCanceled;
            moveAction.action.Enable();
        }
        // lookAction читается напрямую в HandleLook — колбэки не нужны
        if (lookAction != null) lookAction.action.Enable();
        if (jumpAction != null)
        {
            jumpAction.action.performed += OnJumpPerformed;
            jumpAction.action.Enable();
        }
        if (sprintAction != null)
        {
            sprintAction.action.performed += OnSprintPerformed;
            sprintAction.action.canceled += OnSprintCanceled;
            sprintAction.action.Enable();
        }
        if (crouchAction != null)
        {
            crouchAction.action.performed += OnCrouchPerformed;
            crouchAction.action.Enable();
        }
        if (attackAction != null)
        {
            attackAction.action.performed += OnAttackPerformed;
            attackAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (moveAction != null)
        {
            moveAction.action.performed -= OnMovePerformed;
            moveAction.action.canceled -= OnMoveCanceled;
        }
        if (jumpAction != null) jumpAction.action.performed -= OnJumpPerformed;
        if (sprintAction != null)
        {
            sprintAction.action.performed -= OnSprintPerformed;
            sprintAction.action.canceled -= OnSprintCanceled;
        }
        if (crouchAction != null) crouchAction.action.performed -= OnCrouchPerformed;
        if (attackAction != null) attackAction.action.performed -= OnAttackPerformed;
    }

    // =====================================================================
    // Колбэки ввода
    // =====================================================================
    private void OnMovePerformed(InputAction.CallbackContext ctx) => moveInput = ctx.ReadValue<Vector2>();
    private void OnMoveCanceled(InputAction.CallbackContext ctx) => moveInput = Vector2.zero;

    private void OnJumpPerformed(InputAction.CallbackContext ctx) => TryJump();

    private void OnSprintPerformed(InputAction.CallbackContext ctx) => SetSprint(true);
    private void OnSprintCanceled(InputAction.CallbackContext ctx) => SetSprint(false);

    private void OnCrouchPerformed(InputAction.CallbackContext ctx) => SetCrouch(!isCrouching);

    private void OnAttackPerformed(InputAction.CallbackContext ctx) => DoAttack();

    // =====================================================================
    // Публичное API для курсора / блокировки
    // =====================================================================
    /// <summary>
    /// true  — курсор виден, мышь свободна, ввод персонажа заблокирован.
    /// false — курсор скрыт и залочен, управление активно.
    /// </summary>
    public void SetCursorVisible(bool visible)
    {
        Cursor.visible = visible;
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;

        inputLocked = visible;

        if (inputLocked)
        {
            // Сбрасываем накопленный ввод, чтобы персонаж не «доехал» по инерции
            moveInput = Vector2.zero;
            currentMoveVelocity = Vector3.zero;
            smoothMoveVelocity = Vector3.zero;

            // Синхронизируем target с current, чтобы при разблокировке камера не дёрнулась
            targetYaw = currentYaw;
            targetPitch = currentPitch;
            yawVelocity = 0f;
            pitchVelocity = 0f;

            SetSprint(false);

            if (isMoving)
            {
                isMoving = false;
                OnStopMove?.Invoke();
            }
        }
    }

    /// <summary>Удобный переключатель.</summary>
    public void ToggleCursor()
    {
        SetCursorVisible(!Cursor.visible);
    }

    public bool IsInputLocked => inputLocked;

    // =====================================================================
    // Основная логика
    // =====================================================================
    private void Update()
    {
        HandleLook();
        HandleMovement();
        HandleCrouch();
        UpdateAnimator();
    }

    private void HandleLook()
    {
        if (cameraRoot == null || inputLocked) return;

        // Читаем значение НАПРЯМУЮ из действия — так при остановке мыши
        // мы сразу получаем (0,0), и никакой «остаточной» инерции нет.
        Vector2 look = lookAction != null ? lookAction.action.ReadValue<Vector2>() : Vector2.zero;

        targetYaw += look.x * lookSensitivity;
        targetPitch -= look.y * lookSensitivity;
        targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);

        if (lookSmoothTime > 0f)
        {
            currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, lookSmoothTime);
            currentPitch = Mathf.SmoothDampAngle(currentPitch, targetPitch, ref pitchVelocity, lookSmoothTime);
        }
        else
        {
            currentYaw = targetYaw;
            currentPitch = targetPitch;
        }

        transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);
        cameraRoot.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
    }

    private void HandleMovement()
    {
        Vector3 spherePos = transform.position + Vector3.up * (controller.radius + 0.05f);
        isGrounded = controller.isGrounded ||
                     Physics.CheckSphere(spherePos, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore);

        // При заблокированном вводе горизонтального движения нет, но гравитация работает
        Vector3 inputDir = inputLocked
            ? Vector3.zero
            : (transform.right * moveInput.x + transform.forward * moveInput.y);

        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        float targetSpeed = isCrouching ? crouchSpeed : (isSprinting ? sprintSpeed : walkSpeed);
        Vector3 targetVelocity = inputDir * targetSpeed;

        if (moveSmoothTime > 0f)
            currentMoveVelocity = Vector3.SmoothDamp(currentMoveVelocity, targetVelocity, ref smoothMoveVelocity, moveSmoothTime);
        else
            currentMoveVelocity = targetVelocity;

        if (isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        controller.Move((currentMoveVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);

        bool nowMoving = !inputLocked && moveInput.sqrMagnitude > 0.01f;
        if (nowMoving != isMoving)
        {
            isMoving = nowMoving;
            if (isMoving) OnStartMove?.Invoke();
            else OnStopMove?.Invoke();
        }

        if (isGrounded && !wasGrounded) OnLand?.Invoke();
        wasGrounded = isGrounded;
    }

    private void HandleCrouch()
    {
        float targetHeight = isCrouching ? crouchHeight : standHeight;
        float newHeight = Mathf.Lerp(controller.height, targetHeight, crouchSmooth * Time.deltaTime);
        newHeight = Mathf.Clamp(newHeight, crouchHeight, standHeight);

        controller.height = newHeight;
        controller.center = new Vector3(0f, newHeight * 0.5f, 0f);

        if (cameraRoot != null)
        {
            Vector3 camLocal = cameraRoot.localPosition;
            camLocal.y = newHeight - 0.15f;
            cameraRoot.localPosition = Vector3.Lerp(cameraRoot.localPosition, camLocal, crouchSmooth * Time.deltaTime);
        }
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;

        float flatSpeed = new Vector3(currentMoveVelocity.x, 0f, currentMoveVelocity.z).magnitude;
        animator.SetFloat(animSpeed, flatSpeed, 0.1f, Time.deltaTime);
        animator.SetBool(animGrounded, isGrounded);
        animator.SetBool(animCrouch, isCrouching);
    }

    private void TryJump()
    {
        if (inputLocked || !isGrounded) return;
        verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        if (animator != null && !string.IsNullOrEmpty(animJumpTrigger))
            animator.SetTrigger(animJumpTrigger);

        OnJump?.Invoke();
    }

    private void SetSprint(bool value)
    {
        if (isSprinting == value) return;
        isSprinting = value;
        if (value) OnStartSprint?.Invoke();
        else OnStopSprint?.Invoke();
    }

    private void SetCrouch(bool value)
    {
        if (isCrouching == value) return;
        isCrouching = value;
        if (value) OnStartCrouch?.Invoke();
        else OnStopCrouch?.Invoke();
    }

    private void DoAttack()
    {
        if (inputLocked) return;

        if (animator != null && !string.IsNullOrEmpty(animAttackTrigger))
            animator.SetTrigger(animAttackTrigger);

        OnAttack?.Invoke();
    }
}