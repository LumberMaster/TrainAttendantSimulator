using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Game
{
    public class DialogSystem : MonoBehaviour
    {
        public static DialogSystem Instance { get; private set; }

        [Header("References")]
        [SerializeField] private DialogDatabase database;
        [SerializeField] private DialogUI ui;

        [Header("Options")]
        [Tooltip("Прокидывать сообщения о старте/конце диалога в ScenarioSystem.")]
        [SerializeField] private bool sendMessagesToScenario = true;

        [Tooltip("Автоматически подписываться на все ScenarioUnit в сцене при старте.")]
        [SerializeField] private bool autoSubscribeToUnits = true;

        [Header("Events")]
        public UnityEvent<string> OnDialogStart = new UnityEvent<string>();
        public UnityEvent<string> OnDialogEnd = new UnityEvent<string>();
        public UnityEvent<DialogRole, DialogLine> OnDialogLine = new UnityEvent<DialogRole, DialogLine>();

        public DialogDatabase Database => database;
        public bool IsPlaying { get; private set; }
        public DialogSpeaker CurrentSpeaker { get; private set; }
        public Dialog CurrentDialog { get; private set; }

        private Coroutine _routine;
        private bool _skipRequested;

        // Чтобы не подписаться дважды и корректно отписываться
        private readonly Dictionary<ScenarioUnit, DialogSpeaker> _subscribedUnits =
            new Dictionary<ScenarioUnit, DialogSpeaker>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (ui == null) ui = FindObjectOfType<DialogUI>(true);
        }

        private void OnEnable()
        {
            if (autoSubscribeToUnits)
                SubscribeToAllUnits();
        }

        private void OnDisable()
        {
            UnsubscribeFromAllUnits();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------- Публичное API ----------

        public void StartDialog(DialogSpeaker speaker, string dialogId)
        {
            if (database == null)
            {
                Debug.LogWarning("[DialogSystem] DialogDatabase не назначена.");
                return;
            }

            var dialog = database.GetDialog(dialogId);
            if (dialog == null)
            {
                Debug.LogWarning($"[DialogSystem] Диалог '{dialogId}' не найден в базе.");
                return;
            }

            if (IsPlaying) StopDialog();

            _routine = StartCoroutine(PlayRoutine(speaker, dialog));
        }

        public void StopDialog()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            if (CurrentSpeaker != null && CurrentSpeaker.AudioSource != null)
                CurrentSpeaker.AudioSource.Stop();

            ui?.Hide();

            if (IsPlaying)
            {
                var id = CurrentDialog != null ? CurrentDialog.id : string.Empty;
                OnDialogEnd.Invoke(id);
                if (CurrentSpeaker != null)
                    SendScenarioMessage(CurrentSpeaker, CurrentSpeaker.OnDialogEndMessage);
            }

            IsPlaying = false;
            CurrentSpeaker = null;
            CurrentDialog = null;
            _skipRequested = false;
        }

        public void SkipCurrentLine()
        {
            _skipRequested = true;
            if (CurrentSpeaker != null && CurrentSpeaker.AudioSource != null)
                CurrentSpeaker.AudioSource.Stop();
        }

        /// <summary>Подписаться на все ScenarioUnit в сцене (включая выключенные).</summary>
        public void SubscribeToAllUnits()
        {
            var units = FindObjectsOfType<ScenarioUnit>(true);
            foreach (var unit in units) SubscribeToUnit(unit);
        }

        public void SubscribeToUnit(ScenarioUnit unit)
        {
            if (unit == null || _subscribedUnits.ContainsKey(unit)) return;

            var speaker = unit.GetComponent<DialogSpeaker>();
            if (speaker == null) return;

            unit.OnMessageReceived += HandleUnitMessage;
            _subscribedUnits.Add(unit, speaker);
        }

        public void UnsubscribeFromAllUnits()
        {
            foreach (var kv in _subscribedUnits)
                if (kv.Key != null)
                    kv.Key.OnMessageReceived -= HandleUnitMessage;

            _subscribedUnits.Clear();
        }

        // ---------- Внутреннее ----------

        private void HandleUnitMessage(string message)
        {
            // Находим отправителя: единственный ScenarioUnit, который сейчас это прислал.
            // (Предполагаем, что события не приходят одновременно с разных юнитов в одном кадре.)
            DialogSpeaker speaker = null;
            foreach (var kv in _subscribedUnits)
            {
                if (kv.Key == null) continue;

                // Мы не знаем точно, кто прислал — используем UnityEvent-механизм:
                // проще всего проверить, что сообщение пришло именно от этого юнита через ScenarioSystem.
                // Но т.к. OnMessageReceived вызывается прямо из ReceiveMessage(unit),
                // достаточно определить юнит по стеку нельзя. Поэтому находим спикера по совпадению id диалога.
                speaker = kv.Value;
                break;
            }

            if (speaker == null) return;

            if (database == null) return;
            if (database.GetDialog(message) == null) return; // это не id диалога — игнорируем

            StartDialog(speaker, message);
        }

        private IEnumerator PlayRoutine(DialogSpeaker speaker, Dialog dialog)
        {
            IsPlaying = true;
            CurrentSpeaker = speaker;
            CurrentDialog = dialog;

            ui?.Show(speaker, dialog);

            OnDialogStart.Invoke(dialog.id);
            if (sendMessagesToScenario && speaker != null && !string.IsNullOrEmpty(speaker.OnDialogStartMessage))
                SendScenarioMessage(speaker, speaker.OnDialogStartMessage);

            var source = speaker != null ? speaker.AudioSource : null;

            for (int i = 0; i < dialog.lines.Count; i++)
            {
                if (!IsPlaying) yield break;

                var line = dialog.lines[i];
                var role = database.GetRole(line.roleId);

                ui?.SetLine(speaker, role, line);
                OnDialogLine.Invoke(role, line);

                if (line.audio != null && source != null)
                {
                    source.clip = line.audio;
                    source.Play();

                    _skipRequested = false;
                    while (source.isPlaying && !_skipRequested && IsPlaying)
                        yield return null;

                    source.Stop();
                }
                else
                {
                    float dur = line.fallbackDuration > 0 ? line.fallbackDuration : 2f;
                    float t = 0f;
                    _skipRequested = false;
                    while (t < dur && !_skipRequested && IsPlaying)
                    {
                        t += Time.deltaTime;
                        yield return null;
                    }
                }
            }

            IsPlaying = false;
            ui?.Hide();

            OnDialogEnd.Invoke(dialog.id);
            if (speaker != null && !string.IsNullOrEmpty(speaker.OnDialogEndMessage))
                SendScenarioMessage(speaker, speaker.OnDialogEndMessage);

            CurrentSpeaker = null;
            CurrentDialog = null;
            _routine = null;
        }

        private void SendScenarioMessage(DialogSpeaker speaker, string message)
        {
            if (!sendMessagesToScenario) return;
            if (speaker == null || string.IsNullOrEmpty(speaker.UnitName)) return;
            if (ScenarioSystem.Instance == null) return;

            ScenarioSystem.Instance.RecieveMessage(
                new ScenarioMessage(speaker.UnitName, message));
        }
    }
}