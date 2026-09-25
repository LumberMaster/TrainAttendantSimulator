using UnityEngine;
using UnityEngine.Events;

public class NPCCommandTrigger : MonoBehaviour
{
    [SerializeField] private NPCController npc;

    [Header("Команды NPC")]
    public UnityEventVector3 OnMoveTo;
    public UnityEventTransform OnMoveToTarget;
    public UnityEventTransform OnLookAt;
    public UnityEventFloat OnWait;
    public UnityEventString OnPlayAnimation;
    public UnityEvent OnStop;
    public UnityEvent OnClearQueue;

    // --- Методы, которые вы будете назначать в других UnityEvent'ах ---
    public void FireMoveTo(Vector3 pos) => npc?.MoveTo(pos);
    public void FireMoveToTarget(Transform t) => npc?.MoveToTarget(t);
    public void FireLookAt(Transform t) => npc?.LookAtTarget(t);
    public void FireWait(float sec) => npc?.Wait(sec);
    public void FirePlayAnimation(string trig) => npc?.PlayAnimationTrigger(trig);
    public void FireStop() => npc?.StopMovement();
    public void FireClearQueue() => npc?.ClearQueue();

    void Awake()
    {
        OnMoveTo.AddListener(FireMoveTo);
        OnMoveToTarget.AddListener(FireMoveToTarget);
        OnLookAt.AddListener(FireLookAt);
        OnWait.AddListener(FireWait);
        OnPlayAnimation.AddListener(FirePlayAnimation);
        OnStop.AddListener(FireStop);
        OnClearQueue.AddListener(FireClearQueue);
    }
}

// Обёртки, чтобы Unity отрисовала поля аргументов в инспекторе
[System.Serializable] public class UnityEventVector3 : UnityEvent<Vector3> { }
[System.Serializable] public class UnityEventTransform : UnityEvent<Transform> { }
[System.Serializable] public class UnityEventFloat : UnityEvent<float> { }
[System.Serializable] public class UnityEventString : UnityEvent<string> { }