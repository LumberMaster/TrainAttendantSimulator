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
        [SerializeField] private bool sendMessagesToScenario = true;
        [SerializeField] private bool autoSubscribeToUnits = true;
        [SerializeField] private string defaultPlayerRoleId = "";

        [Header("Cursor")]
        [Tooltip("Показывать курсор во время выбора ответа и скрывать после.")]
        [SerializeField] private bool showCursorDuringChoices = true;

        [Header("Events")]
        public UnityEvent<string> OnDialogStart = new UnityEvent<string>();
        public UnityEvent<string> OnDialogEnd = new UnityEvent<string>();
        public UnityEvent<DialogRole, DialogLine> OnDialogLine = new UnityEvent<DialogRole, DialogLine>();

        public DialogDatabase Database => database;
        public bool IsPlaying { get; private set; }
        public DialogSpeaker CurrentSpeaker { get; private set; }
        public DialogAsset CurrentDialog { get; private set; }

        private Coroutine _routine;
        private bool _skipRequested;

        // --- Cursor state ---
        private bool _cursorSaved;
        private bool _savedCursorVisible;
        private CursorLockMode _savedCursorLockMode;

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
            if (autoSubscribeToUnits) SubscribeToAllUnits();
        }

        private void OnDisable() => UnsubscribeFromAllUnits();
        private void OnDestroy()
        {
            RestoreCursor();
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
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }
            if (CurrentSpeaker != null && CurrentSpeaker.AudioSource != null)
                CurrentSpeaker.AudioSource.Stop();

            ui?.Hide();
            ui?.HideChoices();
            ui?.SetTimerActive(false, 0f);

            RestoreCursor();

            if (IsPlaying)
            {
                var id = CurrentDialog != null ? CurrentDialog.id : string.Empty;
                OnDialogEnd.Invoke(id);
                if (CurrentSpeaker != null && !string.IsNullOrEmpty(CurrentSpeaker.OnDialogEndMessage))
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

        private void HandleUnitMessage(ScenarioUnit unit, string message)
        {
            if (!_subscribedUnits.TryGetValue(unit, out var speaker) || speaker == null) return;
            if (database == null) return;
            if (database.GetDialog(message) == null) return;

            StartDialog(speaker, message);
        }

        private IEnumerator PlayRoutine(DialogSpeaker speaker, DialogAsset dialog)
        {
            IsPlaying = true;
            CurrentSpeaker = speaker;
            CurrentDialog = dialog;

            ui?.Show();

            OnDialogStart.Invoke(dialog.id);
            if (sendMessagesToScenario && speaker != null && !string.IsNullOrEmpty(speaker.OnDialogStartMessage))
                SendScenarioMessage(speaker, speaker.OnDialogStartMessage);

            var source = speaker != null ? speaker.AudioSource : null;
            int index = 0;

            while (IsPlaying)
            {
                if (index < 0 || index >= dialog.lines.Count) break;

                var line = dialog.lines[index];
                if (line == null) { index++; continue; }

                var role = database.GetRole(line.roleId);
                ui?.SetLine(role, line);
                OnDialogLine.Invoke(role, line);

                yield return PlayLineAudio(source, line);
                if (!IsPlaying) { RestoreCursor(); yield break; }

                // === Конец диалога на этой реплике ===
                if (line.isEnd)
                {
                    break;
                }

                if (line.transitions != null && line.transitions.Count > 0)
                {
                    int chosenIndex = -1;
                    bool chosen = false;

                    // === Показ выбора: включаем курсор ===
                    ShowCursor();

                    ui?.ShowChoices(line.transitions, i => { chosenIndex = i; chosen = true; });
                    ui?.SetTimerActive(line.useTimer, line.timerDuration);

                    float timeLeft = line.timerDuration;

                    while (IsPlaying && !chosen)
                    {
                        if (line.useTimer)
                        {
                            timeLeft -= Time.deltaTime;
                            ui?.SetTimer(timeLeft);

                            if (timeLeft <= 0f)
                            {
                                chosenIndex = 0;
                                chosen = true;
                                break;
                            }
                        }
                        yield return null;
                    }

                    ui?.HideChoices();
                    ui?.SetTimerActive(false, 0f);

                    // === Выбор сделан: возвращаем курсор в исходное состояние ===
                    RestoreCursor();

                    if (!IsPlaying) yield break;

                    if (chosenIndex < 0 || chosenIndex >= line.transitions.Count) { index++; continue; }

                    var tr = line.transitions[chosenIndex];
                    if (tr == null) { index++; continue; }

                    if (!string.IsNullOrEmpty(tr.choiceText) || tr.choiceAudio != null)
                    {
                        string roleId = !string.IsNullOrEmpty(tr.speakerRoleId)
                            ? tr.speakerRoleId
                            : defaultPlayerRoleId;

                        var choiceLine = new DialogLine
                        {
                            id = "__choice__",
                            roleId = roleId,
                            text = tr.choiceText,
                            audio = tr.choiceAudio,
                            fallbackDuration = 1.2f
                        };

                        var choiceRole = database.GetRole(roleId);
                        ui?.SetLine(choiceRole, choiceLine);
                        OnDialogLine.Invoke(choiceRole, choiceLine);

                        yield return PlayLineAudio(source, choiceLine);
                        if (!IsPlaying) yield break;
                    }

                    int targetIdx = dialog.IndexOfLine(tr.targetLineId);
                    if (targetIdx < 0)
                    {
                        Debug.LogWarning($"[DialogSystem] Transition target '{tr.targetLineId}' не найден в диалоге '{dialog.id}'.");
                        index++;
                    }
                    else index = targetIdx;
                }
                else index++;
            }

            IsPlaying = false;
            ui?.Hide();
            ui?.HideChoices();
            ui?.SetTimerActive(false, 0f);

            RestoreCursor();

            OnDialogEnd.Invoke(dialog.id);
            if (speaker != null && !string.IsNullOrEmpty(speaker.OnDialogEndMessage))
                SendScenarioMessage(speaker, speaker.OnDialogEndMessage);

            CurrentSpeaker = null;
            CurrentDialog = null;
            _routine = null;
        }

        private IEnumerator PlayLineAudio(AudioSource source, DialogLine line)
        {
            _skipRequested = false;

            if (source != null && line.audio != null)
            {
                source.clip = line.audio;
                source.Play();

                while (source.isPlaying && !_skipRequested && IsPlaying)
                    yield return null;

                source.Stop();
            }
            else
            {
                float dur = line.fallbackDuration > 0f ? line.fallbackDuration : 2f;
                float t = 0f;
                while (t < dur && !_skipRequested && IsPlaying)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
            }

            _skipRequested = false;
        }

        private void SendScenarioMessage(DialogSpeaker speaker, string message)
        {
            if (!sendMessagesToScenario) return;
            if (speaker == null || string.IsNullOrEmpty(speaker.UnitName)) return;
            if (ScenarioSystem.Instance == null) return;

            ScenarioSystem.Instance.RecieveMessage(
                new ScenarioMessage(speaker.UnitName, message));
        }

        // ---------- Курсор ----------

        private void ShowCursor()
        {
            if (!showCursorDuringChoices) return;
            if (_cursorSaved) return;

            _savedCursorVisible = Cursor.visible;
            _savedCursorLockMode = Cursor.lockState;
            _cursorSaved = true;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void RestoreCursor()
        {
            if (!_cursorSaved) return;

            Cursor.visible = _savedCursorVisible;
            Cursor.lockState = _savedCursorLockMode;
            _cursorSaved = false;
        }
    }
}