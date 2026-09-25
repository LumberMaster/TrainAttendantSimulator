using UnityEngine;

public enum NPCCommandType
{
    MoveToPosition,   // идти к мировой точке
    MoveToTarget,     // идти к Transform
    LookAtTarget,     // повернуться к Transform
    Wait,             // ждать N секунд
    PlayAnimation,    // проиграть триггер анимации
    Stop              // остановить движение
}

[System.Serializable]
public class NPCCommandData
{
    public NPCCommandType Type = NPCCommandType.MoveToPosition;

    [Tooltip("Мировая точка назначения (MoveToPosition)")]
    public Vector3 TargetPosition;

    [Tooltip("Цель (MoveToTarget / LookAtTarget)")]
    public Transform TargetTransform;

    [Tooltip("Скорость движения")]
    public float MoveSpeed = 2f;

    [Tooltip("Длительность ожидания в секундах (Wait)")]
    public float Duration = 1f;

    [Tooltip("Имя триггера в Animator (PlayAnimation)")]
    public string AnimationTrigger = "Wave";
}