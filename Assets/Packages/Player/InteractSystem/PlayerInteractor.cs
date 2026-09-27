using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[System.Serializable] public class InteractableEvent : UnityEvent<IInteractable> { }

public class PlayerInteractor : MonoBehaviour
{
    [Header("Raycast")]
    [SerializeField] private Camera rayCamera;
    [SerializeField] private float rayDistance = 3f;
    [SerializeField] private LayerMask interactMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Input (New Input System)")]
    [SerializeField] private InputActionReference interactAction;

    [Header("Audio")]
    [Tooltip("AudioSource, из которого проигрывается звук взаимодействия")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Звук взаимодействия (PlayOneShot)")]
    [SerializeField] private AudioClip interactSound;
    [Range(0f, 1f)]
    [SerializeField] private float interactSoundVolume = 1f;

    [Header("Global Events")]
    public InteractableEvent onFocusEnter;
    public InteractableEvent onFocusExit;
    public InteractableEvent onInteract;

    [Header("Global Interactive Focus Events (focus + CanInteract)")]
    public InteractableEvent onInteractiveFocusEnter;
    public InteractableEvent onInteractiveFocusExit;

    private IInteractable currentTarget;
    public IInteractable CurrentTarget => currentTarget;

    // Цель, которая сейчас в фокусе И может быть использована (CanInteract == true)
    private IInteractable interactiveTarget;
    public IInteractable CurrentInteractiveTarget => interactiveTarget;

    public string GetInteractButtonDisplayString()
    {
        if (interactAction == null || interactAction.action == null) return "?";
        return interactAction.action.GetBindingDisplayString();
    }

    private void Awake()
    {
        if (rayCamera == null) rayCamera = Camera.main;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        if (interactAction != null)
        {
            interactAction.action.Enable();
            interactAction.action.performed += OnInteractPerformed;
        }
    }

    private void OnDisable()
    {
        if (interactAction != null)
            interactAction.action.performed -= OnInteractPerformed;
    }

    private void OnInteractPerformed(InputAction.CallbackContext ctx)
    {
        if (currentTarget == null) return;
        if (!currentTarget.CanInteract) return;

        // Звук взаимодействия
        if (audioSource != null && interactSound != null)
            audioSource.PlayOneShot(interactSound, interactSoundVolume);

        currentTarget.OnInteract();
        onInteract?.Invoke(currentTarget);
    }

    private void Update()
    {
        IInteractable newTarget = null;

        if (rayCamera != null)
        {
            Ray ray = new Ray(rayCamera.transform.position, rayCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, interactMask, triggerInteraction))
            {
                var candidate = hit.collider.GetComponentInParent<IInteractable>();
                if (candidate != null && candidate.CanFocus)
                    newTarget = candidate;
            }
        }

        // Если у текущей цели внезапно выключили фокус — сбрасываем её.
        if (currentTarget != null && !currentTarget.CanFocus)
            newTarget = null;

        if (!ReferenceEquals(newTarget, currentTarget))
        {
            // --- Выходим из старой цели ---
            if (currentTarget != null)
            {
                // Сначала гасим "интерактивный фокус", если он был активен
                if (ReferenceEquals(interactiveTarget, currentTarget))
                {
                    currentTarget.OnInteractiveFocusExit();
                    onInteractiveFocusExit?.Invoke(currentTarget);
                    interactiveTarget = null;
                }

                currentTarget.OnFocusExit();
                onFocusExit?.Invoke(currentTarget);
            }

            currentTarget = newTarget;

            // --- Входим в новую цель ---
            if (currentTarget != null)
            {
                currentTarget.OnFocusEnter();
                onFocusEnter?.Invoke(currentTarget);

                if (currentTarget.CanInteract)
                {
                    interactiveTarget = currentTarget;
                    currentTarget.OnInteractiveFocusEnter();
                    onInteractiveFocusEnter?.Invoke(currentTarget);
                }
            }
        }
        else if (currentTarget != null)
        {
            // Фокус не менялся, но CanInteract мог измениться "на лету"
            bool isInteractive = ReferenceEquals(interactiveTarget, currentTarget);

            if (!isInteractive && currentTarget.CanInteract)
            {
                interactiveTarget = currentTarget;
                currentTarget.OnInteractiveFocusEnter();
                onInteractiveFocusEnter?.Invoke(currentTarget);
            }
            else if (isInteractive && !currentTarget.CanInteract)
            {
                currentTarget.OnInteractiveFocusExit();
                onInteractiveFocusExit?.Invoke(currentTarget);
                interactiveTarget = null;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (rayCamera == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(rayCamera.transform.position,
            rayCamera.transform.position + rayCamera.transform.forward * rayDistance);
    }
}