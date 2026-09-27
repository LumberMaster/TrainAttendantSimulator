using UnityEngine;

public enum PassengerTicketStatus
{
    Unknown,      // Неизвестно
    HasTicket,    // Имеет билет
    Ticketless    // Безбилетник
}

public enum PassengerGender
{
    Unknown,
    Male,
    Female
}

public enum PassengerLoyalty
{
    Unknown,
    Hostile,      // Враждебный
    Dissatisfied, // Недовольный
    Neutral,      // Нейтральный
    Satisfied,    // Довольный
    Loyal         // Лояльный
}

/// <summary>
/// Хранит данные о пассажире поезда.
/// Навешивается на объект NPC (или его дочерний объект с коллайдером).
/// Должен реализовывать IInteractable либо находиться на том же объекте,
/// что и другой IInteractable (тогда ищем через GetComponentInParent).
/// </summary>
public class PassengerData : MonoBehaviour
{
    [Header("ФИО")]
    [SerializeField] private string lastName = "Иванов";
    [SerializeField] private string firstName = "Иван";
    [SerializeField] private string middleName = "Иванович";

    [Header("Характеристики")]
    [SerializeField] private PassengerTicketStatus ticketStatus = PassengerTicketStatus.Unknown;
    [SerializeField] private PassengerGender gender = PassengerGender.Unknown;
    [SerializeField, Range(0, 120)] private int age = 30;
    [SerializeField] private PassengerLoyalty loyalty = PassengerLoyalty.Unknown;

    [Header("Дополнительно (опционально)")]
    [SerializeField, TextArea(2, 4)] private string notes = "";

    public string LastName => lastName;
    public string FirstName => firstName;
    public string MiddleName => middleName;
    public PassengerTicketStatus TicketStatus => ticketStatus;
    public PassengerGender Gender => gender;
    public int Age => age;
    public PassengerLoyalty Loyalty => loyalty;
    public string Notes => notes;

    public string FullName =>
        string.IsNullOrWhiteSpace(middleName)
            ? $"{lastName} {firstName}".Trim()
            : $"{lastName} {firstName} {middleName}".Trim();

    // -------- Хелперы для UI --------
    public string GetTicketStatusText()
    {
        switch (ticketStatus)
        {
            case PassengerTicketStatus.HasTicket: return "Имеет билет";
            case PassengerTicketStatus.Ticketless: return "Безбилетник";
            default: return "Неизвестно";
        }
    }

    public string GetGenderText()
    {
        switch (gender)
        {
            case PassengerGender.Male: return "Мужской";
            case PassengerGender.Female: return "Женский";
            default: return "Неизвестно";
        }
    }

    public string GetLoyaltyText()
    {
        switch (loyalty)
        {
            case PassengerLoyalty.Hostile: return "Враждебный";
            case PassengerLoyalty.Dissatisfied: return "Недовольный";
            case PassengerLoyalty.Neutral: return "Нейтральный";
            case PassengerLoyalty.Satisfied: return "Довольный";
            case PassengerLoyalty.Loyal: return "Лояльный";
            default: return "Неизвестно";
        }
    }

    // -------- Публичные сеттеры (для геймплея) --------
    public void SetTicketStatus(PassengerTicketStatus s) => ticketStatus = s;
    public void SetLoyalty(PassengerLoyalty l) => loyalty = l;
}