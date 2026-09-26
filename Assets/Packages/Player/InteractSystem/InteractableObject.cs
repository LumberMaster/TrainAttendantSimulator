using UnityEngine;
using UnityEngine.Events;

public interface IInteractable
{
    string Tooltip { get; }

    bool CanFocus { get; }
    bool CanInteract { get; }

    void OnFocusEnter();
    void OnFocusExit();
    void OnInteract();
}

public class InteractableObject : MonoBehaviour, IInteractable
{
    [Header("Tooltip")]
    [TextArea(2, 4)]
    [Tooltip("Текст подсказки, отображаемый в UI при наведении")]
    [SerializeField] private string tooltip = "Взаимодействовать";

    [Header("State")]
    [Tooltip("Разрешить наведение (focus enter/exit)")]
    [SerializeField] private bool canFocus = true;
    [Tooltip("Разрешить взаимодействие по нажатию")]
    [SerializeField] private bool canInteract = true;

    [Header("Focus")]
    public UnityEvent onFocusEnter;
    public UnityEvent onFocusExit;

    [Header("Interaction")]
    public UnityEvent onInteract;

    public string Tooltip => tooltip;

    public bool CanFocus => canFocus;
    public bool CanInteract => canInteract;

    // --- Публичные методы управления ---

    public void SetFocusEnabled(bool value) => canFocus = value;
    public void SetInteractEnabled(bool value) => canInteract = value;

    public void EnableFocus() => canFocus = true;
    public void DisableFocus() => canFocus = false;

    public void EnableInteract() => canInteract = true;
    public void DisableInteract() => canInteract = false;

    /// <summary>Полностью отключить/включить интерактивность (и фокус, и взаимодействие).</summary>
    public void SetInteractable(bool value)
    {
        canFocus = value;
        canInteract = value;
    }

    // --- Реализация IInteractable ---

    public void OnFocusEnter() => onFocusEnter?.Invoke();
    public void OnFocusExit() => onFocusExit?.Invoke();
    public void OnInteract() => onInteract?.Invoke();
}