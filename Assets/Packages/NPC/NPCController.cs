using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class NPCController : MonoBehaviour
{
    [Header("Сценарий при старте")]
    [Tooltip("Проиграть список команд ниже при запуске сцены")]
    [SerializeField] private bool playScenarioOnStart = false;

    [Tooltip("Список команд NPC — редактируется прямо здесь")]
    [SerializeField] private List<NPCCommandData> startScenario = new List<NPCCommandData>();

    [Header("Стартовая анимация")]
    [Tooltip("Имя состояния (State) в Animator, которое проиграется при запуске сцены. " +
             "Проигрывается принудительно через Animator.Play — переходы не нужны.")]
    [SerializeField] private string startAnimationStateName = "";

    [Tooltip("Слой Animator, на котором играть стартовую анимацию (0 — базовый)")]
    [SerializeField] private int startAnimationLayer = 0;

    [Tooltip("Нормализованное время начала (0 — с начала, 0.5 — с середины)")]
    [Range(0f, 1f)]
    [SerializeField] private float startAnimationNormalizedTime = 0f;

    [Tooltip("Проигрывать стартовую анимацию до запуска сценария команд")]
    [SerializeField] private bool playStartAnimationBeforeScenario = true;

    [Header("Параметры анимации")]
    [Tooltip("Float-параметр Animator: 0 — стоит, 1 — бежит")]
    public string SpeedParam = "Speed";

    [Tooltip("Bool-параметр Animator: true пока NPC занят командой")]
    public string BusyParam = "IsBusy";

    [Header("Настройки движения")]
    public float RotationSpeed = 8f;
    public float ArriveThreshold = 0.2f;
    public float DefaultMoveSpeed = 2f;

    [Header("События (для UnityEvent в инспекторе)")]
    [Tooltip("Вызывается при старте каждой команды. Аргумент — тип команды (int).")]
    public UnityEvent<int> OnCommandStarted;

    [Tooltip("Вызывается, когда вся очередь команд выполнена")]
    public UnityEvent OnAllCommandsCompleted;

    [Tooltip("Вызывается при опустошении очереди или ClearQueue")]
    public UnityEvent OnQueueCleared;

    private NavMeshAgent _agent;
    private Animator _animator;
    private readonly Queue<NPCCommandData> _queue = new Queue<NPCCommandData>();
    private bool _isExecuting;
    private Coroutine _routine;
    private bool _startAnimationPlaying;

    public bool HasCommands => _queue.Count > 0 || _isExecuting;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
    }

    void Start()
    {
        // 1. Принудительно запускаем стартовую анимацию (игнорируя переходы)
        if (!string.IsNullOrEmpty(startAnimationStateName))
        {
            PlayStartAnimation();
        }

        // 2. Если нужно — запускаем сценарий команд
        if (playScenarioOnStart && startScenario.Count > 0)
        {
            if (playStartAnimationBeforeScenario && _startAnimationPlaying)
                StartCoroutine(StartScenarioAfterAnimation());
            else
                EnqueueCommands(startScenario);
        }
    }

    // =========================================================
    //  СТАРТОВАЯ АНИМАЦИЯ
    // =========================================================

    /// <summary>
    /// Принудительно проигрывает указанное состояние Animator,
    /// игнорируя какие-либо переходы и условия.
    /// </summary>
    public void PlayStartAnimation()
    {
        if (string.IsNullOrEmpty(startAnimationStateName))
        {
            Debug.LogWarning($"[NPCController] Имя стартовой анимации не задано на {name}");
            return;
        }

        int stateHash = Animator.StringToHash(startAnimationStateName);

        // Проверяем, существует ли такое состояние (защита от опечаток)
        if (!_animator.HasState(startAnimationLayer, stateHash))
        {
            Debug.LogWarning($"[NPCController] Состояние '{startAnimationStateName}' " +
                             $"не найдено на слое {startAnimationLayer} у {name}");
            return;
        }

        // Принудительный вход в состояние — переходы не нужны
        _animator.Play(stateHash, startAnimationLayer, startAnimationNormalizedTime);
        _animator.Update(0f); // мгновенно применяем

        _startAnimationPlaying = true;
        StartCoroutine(TrackStartAnimation(stateHash));
    }

    private IEnumerator StartScenarioAfterAnimation()
    {
        // Ждём, пока стартовая анимация доиграет
        while (_startAnimationPlaying)
            yield return null;

        if (startScenario.Count > 0)
            EnqueueCommands(startScenario);
    }

    private IEnumerator TrackStartAnimation(int stateHash)
    {
        // Даём Animator один кадр, чтобы переключиться в нужное состояние
        yield return null;

        var stateInfo = _animator.GetCurrentAnimatorStateInfo(startAnimationLayer);

        // Если по какой-то причине не попали в нужное состояние — не блокируем сценарий
        if (stateInfo.shortNameHash != stateHash)
        {
            _startAnimationPlaying = false;
            yield break;
        }

        float length = stateInfo.length;

        // Если анимация зациклена — не ждём её окончания
        if (stateInfo.loop && length > 0.01f)
        {
            _startAnimationPlaying = false;
            yield break;
        }

        // Ждём завершения (с небольшим запасом)
        if (length > 0.01f && length < 60f)
            yield return new WaitForSeconds(length);

        _startAnimationPlaying = false;
    }

    void Update()
    {
        // Пока играет стартовая анимация без переходов — не трогаем Speed,
        // чтобы не сбить её через параметры Animator.
        if (!_startAnimationPlaying)
        {
            float speed01 = _agent.velocity.magnitude / Mathf.Max(_agent.speed, 0.01f);
            _animator.SetFloat(SpeedParam, speed01, 0.1f, Time.deltaTime);
        }
    }

    // =========================================================
    //  ПУБЛИЧНОЕ API — эти методы можно назначать в UnityEvents
    // =========================================================

    // --- Простые обёртки (одно поле аргумента — удобно для UnityEvent) ---

    public void MoveTo(Vector3 position) => Enqueue(new NPCCommandData { Type = NPCCommandType.MoveToPosition, TargetPosition = position, MoveSpeed = DefaultMoveSpeed });
    public void MoveToTarget(Transform target) => Enqueue(new NPCCommandData { Type = NPCCommandType.MoveToTarget, TargetTransform = target, MoveSpeed = DefaultMoveSpeed });
    public void LookAtTarget(Transform target) => Enqueue(new NPCCommandData { Type = NPCCommandType.LookAtTarget, TargetTransform = target });
    public void Wait(float seconds) => Enqueue(new NPCCommandData { Type = NPCCommandType.Wait, Duration = seconds });
    public void PlayAnimationTrigger(string trig) => Enqueue(new NPCCommandData { Type = NPCCommandType.PlayAnimation, AnimationTrigger = trig });
    public void StopMovement() => Enqueue(new NPCCommandData { Type = NPCCommandType.Stop });

    // --- Работа с очередью ---

    public void Enqueue(NPCCommandData cmd)
    {
        if (cmd == null) return;
        _queue.Enqueue(cmd);
        if (!_isExecuting) _routine = StartCoroutine(ExecuteQueue());
    }

    public void EnqueueCommands(List<NPCCommandData> cmds)
    {
        if (cmds == null) return;
        foreach (var c in cmds) if (c != null) _queue.Enqueue(c);
        if (!_isExecuting) _routine = StartCoroutine(ExecuteQueue());
    }

    public void ClearQueue()
    {
        _queue.Clear();
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        _isExecuting = false;
        _agent.ResetPath();
        _animator.SetBool(BusyParam, false);
        OnQueueCleared?.Invoke();
    }

    // =========================================================
    //  Внутренняя логика
    // =========================================================

    private IEnumerator ExecuteQueue()
    {
        _isExecuting = true;

        while (_queue.Count > 0)
        {
            var cmd = _queue.Dequeue();
            _animator.SetBool(BusyParam, true);
            OnCommandStarted?.Invoke((int)cmd.Type);

            switch (cmd.Type)
            {
                case NPCCommandType.MoveToPosition:
                case NPCCommandType.MoveToTarget:
                    yield return HandleMoveTo(cmd);
                    break;

                case NPCCommandType.Wait:
                    yield return new WaitForSeconds(cmd.Duration);
                    break;

                case NPCCommandType.PlayAnimation:
                    yield return HandlePlayAnimation(cmd);
                    break;

                case NPCCommandType.LookAtTarget:
                    yield return HandleLookAt(cmd);
                    break;

                case NPCCommandType.Stop:
                    _agent.ResetPath();
                    break;
            }
        }

        _agent.ResetPath();
        _animator.SetBool(BusyParam, false);
        _isExecuting = false;
        _routine = null;
        OnAllCommandsCompleted?.Invoke();
    }

    private IEnumerator HandleMoveTo(NPCCommandData cmd)
    {
        Vector3 dest = cmd.TargetTransform != null ? cmd.TargetTransform.position : cmd.TargetPosition;

        _agent.speed = cmd.MoveSpeed > 0 ? cmd.MoveSpeed : DefaultMoveSpeed;
        _agent.isStopped = false;
        _agent.SetDestination(dest);

        yield return null;

        while (!_agent.pathPending &&
               _agent.remainingDistance > _agent.stoppingDistance + ArriveThreshold)
            yield return null;

        while (_agent.velocity.sqrMagnitude > 0.01f)
            yield return null;

        _agent.isStopped = true;
    }

    private IEnumerator HandlePlayAnimation(NPCCommandData cmd)
    {
        if (string.IsNullOrEmpty(cmd.AnimationTrigger)) yield break;

        _animator.SetTrigger(cmd.AnimationTrigger);
        yield return null;

        float len = _animator.GetCurrentAnimatorStateInfo(0).length;
        if (len > 0.01f && len < 30f)
            yield return new WaitForSeconds(len);
    }

    private IEnumerator HandleLookAt(NPCCommandData cmd)
    {
        if (cmd.TargetTransform == null) yield break;

        Quaternion targetRot;
        do
        {
            Vector3 dir = cmd.TargetTransform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) break;

            targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot, RotationSpeed * Time.deltaTime);

            yield return null;
        }
        while (Quaternion.Angle(transform.rotation, targetRot) > 1f);
    }
}