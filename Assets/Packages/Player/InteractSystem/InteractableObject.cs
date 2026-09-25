using UnityEngine;
using UnityEngine.Events;

public interface IInteractable
{
    void OnFocusEnter();
    void OnFocusExit();
    void OnInteract();
}

public class InteractableObject : MonoBehaviour, IInteractable
{
    [Header("Focus")]
    [Tooltip("Вызывается, когда игрок навёл луч на объект")]
    public UnityEvent onFocusEnter;
    [Tooltip("Вызывается, когда игрок убрал луч с объекта")]
    public UnityEvent onFocusExit;

    [Header("Interaction")]
    [Tooltip("Вызывается при нажатии Interact, пока игрок смотрит на объект")]
    public UnityEvent onInteract;

    public void OnFocusEnter() => onFocusEnter?.Invoke();
    public void OnFocusExit() => onFocusExit?.Invoke();
    public void OnInteract() => onInteract?.Invoke();
}