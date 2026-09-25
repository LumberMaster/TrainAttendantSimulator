using System.Collections;
using TMPro;
using UnityEngine;

public class InteractionTooltipUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInteractor playerInteractor;

    [Tooltip("Корневой объект подсказки (панель)")]
    [SerializeField] private GameObject root;

    [Tooltip("Текст подсказки (например, 'Открыть дверь')")]
    [SerializeField] private TMP_Text tooltipText;

    [Tooltip("Текст кнопки — статичный, например 'Press E'")]
    [SerializeField] private TMP_Text buttonText;

    [Tooltip("Трансформ курсора (UI Image / спрайт), который увеличивается при наведении")]
    [SerializeField] private RectTransform cursorTransform;

    [Header("Cursor")]
    [SerializeField] private float cursorHoverScale = 1.3f;
    [SerializeField] private float cursorLerpSpeed = 10f;

    [Header("Static Button Text")]
    [SerializeField] private string buttonStaticText = "Press E";

    [Header("Tooltip Punch Animation (on Interact)")]
    [Tooltip("Насколько раздувается текст подсказки при нажатии")]
    [SerializeField] private float punchScale = 1.25f;
    [SerializeField] private float punchDuration = 0.15f;

    [Header("Startup")]
    [SerializeField] private bool hideOnStart = true;

    private Vector3 cursorBaseScale = Vector3.one;
    private Vector3 tooltipBaseScale = Vector3.one;
    private Coroutine punchRoutine;

    private void Awake()
    {
        if (playerInteractor == null)
            playerInteractor = FindFirstObjectByType<PlayerInteractor>();

        if (cursorTransform != null)
            cursorBaseScale = cursorTransform.localScale;

        if (tooltipText != null)
            tooltipBaseScale = tooltipText.transform.localScale;
    }

    private void OnEnable()
    {
        if (playerInteractor == null) return;

        playerInteractor.onFocusEnter.AddListener(HandleFocusEnter);
        playerInteractor.onFocusExit.AddListener(HandleFocusExit);
        playerInteractor.onInteract.AddListener(HandleInteract);

        if (buttonText != null)
            buttonText.text = buttonStaticText;

        if (hideOnStart) SetVisible(false);
    }

    private void OnDisable()
    {
        if (playerInteractor == null) return;

        playerInteractor.onFocusEnter.RemoveListener(HandleFocusEnter);
        playerInteractor.onFocusExit.RemoveListener(HandleFocusExit);
        playerInteractor.onInteract.RemoveListener(HandleInteract);
    }

    private void Update()
    {
        if (cursorTransform == null) return;

        Vector3 target = (playerInteractor != null && playerInteractor.CurrentTarget != null)
            ? cursorBaseScale * cursorHoverScale
            : cursorBaseScale;

        cursorTransform.localScale = Vector3.Lerp(
            cursorTransform.localScale, target, Time.unscaledDeltaTime * cursorLerpSpeed);
    }

    private void HandleFocusEnter(IInteractable target)
    {
        if (target == null) return;

        if (tooltipText != null)
            tooltipText.text = string.IsNullOrEmpty(target.Tooltip) ? "" : target.Tooltip;

        if (buttonText != null)
            buttonText.text = buttonStaticText;

        SetVisible(true);
    }

    private void HandleFocusExit(IInteractable target)
    {
        SetVisible(false);

        // Возвращаем масштаб текста подсказки на случай, если анимация не успела завершиться
        if (punchRoutine != null)
        {
            StopCoroutine(punchRoutine);
            punchRoutine = null;
        }
        if (tooltipText != null)
            tooltipText.transform.localScale = tooltipBaseScale;
    }

    private void HandleInteract(IInteractable target)
    {
        if (tooltipText == null) return;

        if (punchRoutine != null) StopCoroutine(punchRoutine);
        punchRoutine = StartCoroutine(PunchTooltip());
    }

    private IEnumerator PunchTooltip()
    {
        Transform t = tooltipText.transform;
        float half = punchDuration * 0.5f;

        // Раздув
        float time = 0f;
        while (time < half)
        {
            time += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(time / half);
            t.localScale = Vector3.Lerp(tooltipBaseScale, tooltipBaseScale * punchScale, k);
            yield return null;
        }

        // Возврат
        time = 0f;
        while (time < half)
        {
            time += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(time / half);
            t.localScale = Vector3.Lerp(tooltipBaseScale * punchScale, tooltipBaseScale, k);
            yield return null;
        }

        t.localScale = tooltipBaseScale;
        punchRoutine = null;
    }

    private void SetVisible(bool visible)
    {
        if (root != null) root.SetActive(visible);
        else
        {
            if (tooltipText != null) tooltipText.enabled = visible;
            if (buttonText != null) buttonText.enabled = visible;
        }
    }
}