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

    private IInteractable currentTarget;
    public IInteractable CurrentTarget => currentTarget;

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
                newTarget = hit.collider.GetComponentInParent<IInteractable>();
        }

        if (!ReferenceEquals(newTarget, currentTarget))
        {
            if (currentTarget != null)
            {
                currentTarget.OnFocusExit();
                onFocusExit?.Invoke(currentTarget);
            }

            currentTarget = newTarget;

            if (currentTarget != null)
            {
                currentTarget.OnFocusEnter();
                onFocusEnter?.Invoke(currentTarget);
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