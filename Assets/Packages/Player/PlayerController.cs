using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerController : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Пивот головы (Follow/LookAt target у Cinemachine Virtual Camera)")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Animator animator;

    [Header("Input Actions")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference sprintAction;
    [SerializeField] private InputActionReference crouchAction;
    [SerializeField] private InputActionReference attackAction;
    [Tooltip("Опционально: клавиша выхода из UI-режима (Esc)")]
    [SerializeField] private InputActionReference uiExitAction;

    [Header("Движение")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float sprintSpeed = 6f;
    [SerializeField] private float crouchSpeed = 1.5f;
    [SerializeField] private float moveSmoothTime = 0.12f;

    [Header("Прыжок и гравитация")]
    [SerializeField] private float jumpHeight = 1.1f;
    [Tooltip("Только для расчёта стартовой скорости прыжка. Саму гравитацию считает Rigidbody (Physics.gravity).")]
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundCheckRadius = 0.15f;
    [Tooltip("Небольшой зазор вниз для проверки опоры")]
    [SerializeField] private float groundCheckOffset = 0.05f;
    [SerializeField] private LayerMask groundMask = ~0;
    [Tooltip("Сила, прижимающая к земле, чтобы isGrounded не мерцал")]
    [SerializeField] private float groundStickForce = -2f;

    [Header("Приседание")]
    [SerializeField] private float standHeight = 1.8f;
    [SerializeField] private float crouchHeight = 1.0f;
    [SerializeField] private float crouchSmooth = 8f;
    [SerializeField] private float pivotStandHeight = 1.65f;
    [SerializeField] private float pivotCrouchHeight = 0.9f;

    [Header("Камера")]
    [SerializeField] private float lookSensitivity = 0.15f;
    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;
    [SerializeField] private float lookSmoothTime = 0.05f;

    [Header("Курсор")]
    [SerializeField] private bool lockCursorOnStart = true;

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
    public UnityEvent OnEnterUIMode;
    public UnityEvent OnExitUIMode;

    public bool IsUIMode { get; private set; }

    private Rigidbody rb;
    private CapsuleCollider capsule;

    private Vector2 moveInput;
    private Vector2 lookInput;

    private Vector3 smoothMoveVelocity;
    private Vector3 currentMoveVelocity;
    private bool isGrounded, wasGrounded, isSprinting, isCrouching, isMoving;

    private float currentYaw, targetYaw, currentPitch, targetPitch, yawVelocity, pitchVelocity;

    // =====================================================================
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        // Вращение — только через скрипт; падение — силами Rigidbody
        rb.freezeRotation = true;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (cameraPivot == null)
        {
            var go = new GameObject("CameraPivot");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0f, pivotStandHeight, 0f);
            cameraPivot = go.transform;
        }

        currentYaw = targetYaw = transform.eulerAngles.y;
        currentPitch = targetPitch = 0f;

        capsule.height = standHeight;
        capsule.center = new Vector3(0f, standHeight * 0.5f, 0f);
    }

    private void OnEnable()
    {
        Subscribe(moveAction, OnMovePerformed, OnMoveCanceled);
        Subscribe(lookAction, OnLookPerformed);
        Subscribe(jumpAction, OnJumpPerformed);
        Subscribe(sprintAction, OnSprintPerformed, OnSprintCanceled);
        Subscribe(crouchAction, OnCrouchPerformed);
        Subscribe(attackAction, OnAttackPerformed);
        Subscribe(uiExitAction, OnUiExitPerformed);
    }

    private void OnDisable()
    {
        Unsubscribe(moveAction, OnMovePerformed, OnMoveCanceled);
        Unsubscribe(lookAction, OnLookPerformed);
        Unsubscribe(jumpAction, OnJumpPerformed);
        Unsubscribe(sprintAction, OnSprintPerformed, OnSprintCanceled);
        Unsubscribe(crouchAction, OnCrouchPerformed);
        Unsubscribe(attackAction, OnAttackPerformed);
        Unsubscribe(uiExitAction, OnUiExitPerformed);
    }

    private void Start()
    {
        if (lockCursorOnStart)
        {
            IsUIMode = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        if (IsUIMode)
        {
            moveInput = Vector2.zero;
            lookInput = Vector2.zero;
        }
        else
        {
            HandleLook();
        }

        HandleCrouch();
        UpdateAnimator();
    }

    private void FixedUpdate()
    {
        CheckGround();
        ApplyMovement();
    }

    // =====================================================================
    // Ввод
    // =====================================================================
    private void Subscribe(InputActionReference r, System.Action<InputAction.CallbackContext> performed,
                           System.Action<InputAction.CallbackContext> canceled = null)
    {
        if (r == null) return;
        r.action.performed += performed;
        if (canceled != null) r.action.canceled += canceled;
        r.action.Enable();
    }

    private void Unsubscribe(InputActionReference r, System.Action<InputAction.CallbackContext> performed,
                             System.Action<InputAction.CallbackContext> canceled = null)
    {
        if (r == null) return;
        r.action.performed -= performed;
        if (canceled != null) r.action.canceled -= canceled;
    }

    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        if (IsUIMode) return;
        moveInput = ctx.ReadValue<Vector2>();
    }
    private void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        moveInput = Vector2.zero;
    }

    private void OnLookPerformed(InputAction.CallbackContext ctx)
    {
        if (IsUIMode) return;
        // Накапливаем — за кадр может прилететь несколько событий.
        lookInput += ctx.ReadValue<Vector2>();
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (IsUIMode) return;
        TryJump();
    }

    private void OnSprintPerformed(InputAction.CallbackContext ctx)
    {
        if (IsUIMode) return;
        SetSprint(true);
    }
    private void OnSprintCanceled(InputAction.CallbackContext ctx)
    {
        SetSprint(false);
    }

    private void OnCrouchPerformed(InputAction.CallbackContext ctx)
    {
        if (IsUIMode) return;
        SetCrouch(!isCrouching);
    }

    private void OnAttackPerformed(InputAction.CallbackContext ctx)
    {
        if (IsUIMode) return;
        DoAttack();
    }

    private void OnUiExitPerformed(InputAction.CallbackContext ctx)
    {
        if (IsUIMode) ExitUIMode();
    }

    // =====================================================================
    // Режим UI
    // =====================================================================
    public void EnterUIMode() => SetUIMode(true);
    public void ExitUIMode() => SetUIMode(false);
    public void ToggleUIMode() => SetUIMode(!IsUIMode);

    public void SetUIMode(bool uiMode)
    {
        if (IsUIMode == uiMode) return;
        IsUIMode = uiMode;

        if (uiMode)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            moveInput = Vector2.zero;
            lookInput = Vector2.zero;
            currentMoveVelocity = Vector3.zero;
            smoothMoveVelocity = Vector3.zero;

            SetSprint(false);
            SetCrouch(false);

            OnEnterUIMode?.Invoke();
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            targetYaw = currentYaw = transform.eulerAngles.y;
            targetPitch = currentPitch;

            OnExitUIMode?.Invoke();
        }
    }

    // =====================================================================
    // Логика
    // =====================================================================
    private void HandleLook()
    {
        if (cameraPivot == null) return;

        targetYaw += lookInput.x * lookSensitivity;
        targetPitch -= lookInput.y * lookSensitivity;
        targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);

        // ВАЖНО: обнуляем накопленный ввод. Иначе, если мышь/стик
        // остановились и performed перестал приходить, lookInput останется
        // с последним значением — и персонаж будет крутиться «по инерции».
        lookInput = Vector2.zero;

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
        cameraPivot.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
    }

    private void CheckGround()
    {
        if (capsule == null) { isGrounded = false; return; }

        // Локальная Y нижней сферы капсулы = capsule.radius,
        // поэтому позиция проверки не зависит от текущей высоты (приседания).
        Vector3 sphereCenter = transform.position + transform.rotation *
            (capsule.center + Vector3.down * (capsule.height * 0.5f - capsule.radius));
        sphereCenter += Vector3.down * groundCheckOffset;

        float radius = Mathf.Max(groundCheckRadius, capsule.radius * 0.9f);
        isGrounded = Physics.CheckSphere(sphereCenter, radius, groundMask, QueryTriggerInteraction.Ignore);

        if (isGrounded && !wasGrounded) OnLand?.Invoke();
        wasGrounded = isGrounded;
    }

    private void ApplyMovement()
    {
        Vector3 inputDir = transform.right * moveInput.x + transform.forward * moveInput.y;
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        float targetSpeed = isCrouching ? crouchSpeed : (isSprinting ? sprintSpeed : walkSpeed);
        Vector3 targetVelocity = inputDir * targetSpeed;

        Vector3 currentHoriz = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        if (moveSmoothTime > 0f)
            currentMoveVelocity = Vector3.SmoothDamp(currentHoriz, targetVelocity, ref smoothMoveVelocity, moveSmoothTime);
        else
            currentMoveVelocity = targetVelocity;

        Vector3 vel = rb.linearVelocity;
        vel.x = currentMoveVelocity.x;
        vel.z = currentMoveVelocity.z;

        // Прижим к земле — чтобы isGrounded не мерцал и персонаж не «парил».
        // Не трогаем положительный vel.y, чтобы не сорвать прыжок.
        if (isGrounded && vel.y < 0f)
            vel.y = groundStickForce;

        rb.linearVelocity = vel;

        bool nowMoving = moveInput.sqrMagnitude > 0.01f;
        if (nowMoving != isMoving)
        {
            isMoving = nowMoving;
            if (isMoving) OnStartMove?.Invoke();
            else OnStopMove?.Invoke();
        }
    }

    private void HandleCrouch()
    {
        float targetHeight = isCrouching ? crouchHeight : standHeight;
        float newHeight = Mathf.Lerp(capsule.height, targetHeight, crouchSmooth * Time.deltaTime);
        newHeight = Mathf.Clamp(newHeight, crouchHeight, standHeight);

        capsule.height = newHeight;
        capsule.center = new Vector3(0f, newHeight * 0.5f, 0f);

        if (cameraPivot != null)
        {
            float targetPivotY = isCrouching ? pivotCrouchHeight : pivotStandHeight;
            Vector3 p = cameraPivot.localPosition;
            p.y = Mathf.Lerp(p.y, targetPivotY, crouchSmooth * Time.deltaTime);
            cameraPivot.localPosition = p;
        }
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;

        Vector3 horiz = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        animator.SetFloat(animSpeed, horiz.magnitude, 0.1f, Time.deltaTime);
        animator.SetBool(animGrounded, isGrounded);
        animator.SetBool(animCrouch, isCrouching);
    }

    // =====================================================================
    // Действия
    // =====================================================================
    private void TryJump()
    {
        if (!isGrounded) return;

        Vector3 vel = rb.linearVelocity;
        vel.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        rb.linearVelocity = vel;

        // Чтобы не поймать второй прыжок в том же кадре до следующего CheckGround
        isGrounded = false;

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
        if (animator != null && !string.IsNullOrEmpty(animAttackTrigger))
            animator.SetTrigger(animAttackTrigger);

        OnAttack?.Invoke();
    }
}