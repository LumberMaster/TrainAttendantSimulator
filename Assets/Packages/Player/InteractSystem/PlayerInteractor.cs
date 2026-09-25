using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[System.Serializable] public class InteractableEvent : UnityEvent<IInteractable> { }

public class PlayerInteractor : MonoBehaviour
{
    [Header("Raycast")]
    [Tooltip("Камера, из которой пускаем луч (обычно Main Camera)")]
    [SerializeField] private Camera rayCamera;
    [SerializeField] private float rayDistance = 3f;
    [Tooltip("Слои, которые считаем интерактивными")]
    [SerializeField] private LayerMask interactMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Input (New Input System)")]
    [Tooltip("Ссылка на Action типа Button, например Player/Interact")]
    [SerializeField] private InputActionReference interactAction;

    [Header("Global Events (необязательно)")]
    public InteractableEvent onFocusEnter;
    public InteractableEvent onFocusExit;
    public InteractableEvent onInteract;

    private IInteractable currentTarget;

    public IInteractable CurrentTarget => currentTarget;

    private void Awake()
    {
        if (rayCamera == null) rayCamera = Camera.main;
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
        {
            interactAction.action.performed -= OnInteractPerformed;
        }
    }

    private void OnInteractPerformed(InputAction.CallbackContext ctx)
    {
        if (currentTarget == null) return;

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
                // Работает и если коллайдер на дочернем объекте
                newTarget = hit.collider.GetComponentInParent<IInteractable>();
            }
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