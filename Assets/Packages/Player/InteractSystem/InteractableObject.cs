using UnityEngine;
using UnityEngine.Events;

public interface IInteractable
{
    string Tooltip { get; }

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

    [Header("Focus")]
    public UnityEvent onFocusEnter;
    public UnityEvent onFocusExit;

    [Header("Interaction")]
    public UnityEvent onInteract;

    public string Tooltip => tooltip;

    public void OnFocusEnter() => onFocusEnter?.Invoke();
    public void OnFocusExit() => onFocusExit?.Invoke();
    public void OnInteract() => onInteract?.Invoke();
}