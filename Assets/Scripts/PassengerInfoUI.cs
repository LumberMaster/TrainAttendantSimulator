using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Подписывается на события PlayerInteractor.
/// Когда игрок наводится на объект — ищет на нём (или в родителях) PassengerData
/// и показывает панель с информацией. При потере фокуса — прячет.
/// </summary>
public class PassengerInfoUI : MonoBehaviour
{
    [Header("Источник событий")]
    [SerializeField] private PlayerInteractor playerInteractor;

    [Header("Корневая панель")]
    [SerializeField] private GameObject panelRoot;

    [Header("Текстовые поля")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text ticketStatusText;
    [SerializeField] private TMP_Text genderText;
    [SerializeField] private TMP_Text ageText;
    [SerializeField] private TMP_Text loyaltyText;
    [SerializeField] private TMP_Text notesText;

    [Header("Иконки статуса (опционально)")]
    [SerializeField] private Image loyaltyIcon;
    [SerializeField] private Sprite loyaltyHostileIcon;
    [SerializeField] private Sprite loyaltyDissatisfiedIcon;
    [SerializeField] private Sprite loyaltyNeutralIcon;
    [SerializeField] private Sprite loyaltySatisfiedIcon;
    [SerializeField] private Sprite loyaltyLoyalIcon;

    [Header("Поведение")]
    [Tooltip("Прятать панель при старте сцены")]
    [SerializeField] private bool hideOnStart = true;

    private PassengerData currentData;

    private void Reset()
    {
        if (playerInteractor == null)
            playerInteractor = FindObjectOfType<PlayerInteractor>();
    }

    private void Awake()
    {
        if (playerInteractor == null)
            playerInteractor = FindObjectOfType<PlayerInteractor>();

        if (hideOnStart && panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (playerInteractor == null)
        {
            Debug.LogWarning($"{nameof(PassengerInfoUI)}: PlayerInteractor не назначен.", this);
            return;
        }

        playerInteractor.onFocusEnter.AddListener(HandleFocusEnter);
        playerInteractor.onFocusExit.AddListener(HandleFocusExit);
        playerInteractor.onInteract.AddListener(HandleInteract);
    }

    private void OnDisable()
    {
        if (playerInteractor == null) return;

        playerInteractor.onFocusEnter.RemoveListener(HandleFocusEnter);
        playerInteractor.onFocusExit.RemoveListener(HandleFocusExit);
        playerInteractor.onInteract.RemoveListener(HandleInteract);
    }

    private void HandleFocusEnter(IInteractable interactable)
    {
        PassengerData data = ExtractPassengerData(interactable);
        if (data == null)
        {
            Hide();
            return;
        }

        currentData = data;
        Show(data);
    }

    private void HandleFocusExit(IInteractable interactable)
    {
        // Если ушли с того же пассажира — скрываем.
        // Проверяем на всякий случай, чтобы не сбить фокус с нового NPC.
        var data = ExtractPassengerData(interactable);
        if (data != null && data == currentData)
        {
            currentData = null;
            Hide();
        }
        else if (data == null && currentData != null)
        {
            // На всякий случай: если фокус ушёл с не-NPC — прячем.
            currentData = null;
            Hide();
        }
    }

    private void HandleInteract(IInteractable interactable)
    {
        // Здесь можно, например, "опросить" пассажира и обновить данные.
        // Пока оставим как заготовку.
        if (currentData != null && ExtractPassengerData(interactable) == currentData)
        {
            Refresh();
        }
    }

    /// <summary>
    /// IInteractable может быть на компоненте, а PassengerData — на родителе/том же объекте.
    /// </summary>
    private PassengerData ExtractPassengerData(IInteractable interactable)
    {
        if (interactable == null) return null;

        // IInteractable может быть реализован MonoBehaviour-ом.
        if (interactable is Component comp)
        {
            var data = comp.GetComponentInParent<PassengerData>();
            if (data != null) return data;
            // На случай, если данные висят на дочернем объекте
            data = comp.GetComponentInChildren<PassengerData>();
            return data;
        }

        return null;
    }

    private void Show(PassengerData data)
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);

        Refresh();
    }

    private void Refresh()
    {
        if (currentData == null) return;

        if (nameText != null) nameText.text = currentData.FullName;
        if (ticketStatusText != null) ticketStatusText.text = $"Билет: {currentData.GetTicketStatusText()}";
        if (genderText != null) genderText.text = $"Пол: {currentData.GetGenderText()}";
        if (ageText != null) ageText.text = $"Возраст: {currentData.Age}";
        if (loyaltyText != null) loyaltyText.text = $"Лояльность: {currentData.GetLoyaltyText()}";

        if (notesText != null)
        {
            bool hasNotes = !string.IsNullOrWhiteSpace(currentData.Notes);
            notesText.gameObject.SetActive(hasNotes);
            if (hasNotes) notesText.text = currentData.Notes;
        }

        UpdateLoyaltyIcon(currentData.Loyalty);
    }

    private void UpdateLoyaltyIcon(PassengerLoyalty loyalty)
    {
        if (loyaltyIcon == null) return;

        Sprite sprite = loyalty switch
        {
            PassengerLoyalty.Hostile => loyaltyHostileIcon,
            PassengerLoyalty.Dissatisfied => loyaltyDissatisfiedIcon,
            PassengerLoyalty.Neutral => loyaltyNeutralIcon,
            PassengerLoyalty.Satisfied => loyaltySatisfiedIcon,
            PassengerLoyalty.Loyal => loyaltyLoyalIcon,
            _ => null
        };

        loyaltyIcon.gameObject.SetActive(sprite != null);
        if (sprite != null) loyaltyIcon.sprite = sprite;
    }

    private void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }
}