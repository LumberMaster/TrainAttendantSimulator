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

        [Header("Portrait Render")]
        [Tooltip("RenderTexture, в которую рендерит камера текущего говорящего. Отображается в DialogUI.")]
        [SerializeField] private RenderTexture dialogRenderTexture;

        [Header("History")]
        [Tooltip("Писать историю диалогов в DialogHistory (если он есть в сцене).")]
        [SerializeField] private bool recordHistory = true;

        [Header("Events")]
        public UnityEvent<string> OnDialogStart = new UnityEvent<string>();
        public UnityEvent<string> OnDialogEnd = new UnityEvent<string>();
        public UnityEvent<DialogRole, DialogLine> OnDialogLine = new UnityEvent<DialogRole, DialogLine>();

        public DialogDatabase Database => database;
        public bool IsPlaying { get; private set; }
        public DialogSpeaker CurrentSpeaker { get; private set; }
        public DialogAsset CurrentDialog { get; private set; }
        public RenderTexture DialogRenderTexture => dialogRenderTexture;

        private Coroutine _routine;
        private bool _skipRequested;

        // --- Cursor state ---
        private bool _cursorSaved;
        private bool _savedCursorVisible;
        private CursorLockMode _savedCursorLockMode;

        // --- Portrait / camera state ---
        private Camera _swappedCamera;
        private RenderTexture _swappedCameraOriginalTarget;

        private readonly Dictionary<ScenarioUnit, DialogSpeaker> _subscribedUnits =
            new Dictionary<ScenarioUnit, DialogSpeaker>();

        private readonly Dictionary<string, DialogSpeaker> _speakersById =
            new Dictionary<string, DialogSpeaker>();

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
            ClearActivePortrait();
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
            ClearActivePortrait();

            if (IsPlaying)
            {
                var id = CurrentDialog != null ? CurrentDialog.id : string.Empty;
                OnDialogEnd.Invoke(id);
                if (CurrentSpeaker != null && !string.IsNullOrEmpty(CurrentSpeaker.OnDialogEndMessage))
                    SendScenarioMessage(CurrentSpeaker, CurrentSpeaker.OnDialogEndMessage);

                if (recordHistory) DialogHistory.Instance?.EndDialog();
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

        public DialogSpeaker GetSpeakerById(string speakerId)
        {
            if (string.IsNullOrEmpty(speakerId)) return null;
            return _speakersById.TryGetValue(speakerId, out var s) ? s : null;
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

            if (!string.IsNullOrEmpty(speaker.SpeakerId))
                _speakersById[speaker.SpeakerId] = speaker;
        }

        public void UnsubscribeFromAllUnits()
        {
            foreach (var kv in _subscribedUnits)
                if (kv.Key != null)
                    kv.Key.OnMessageReceived -= HandleUnitMessage;

            _subscribedUnits.Clear();
            _speakersById.Clear();
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

            if (recordHistory)
                DialogHistory.Instance?.BeginDialog(dialog.id, speaker != null ? speaker.SpeakerId : null);

            var source = speaker != null ? speaker.AudioSource : null;
            int index = 0;

            while (IsPlaying)
            {
                if (index < 0 || index >= dialog.lines.Count) break;

                var line = dialog.lines[index];
                if (line == null) { index++; continue; }

                // Портрет — по roleId реплики
                SetActivePortraitForRole(line.roleId);

                var role = database.GetRole(line.roleId);
                ui?.SetLine(role, line);
                OnDialogLine.Invoke(role, line);

                // === Запись реплики в историю (без параметров — они теперь на переходах) ===
                if (recordHistory)
                    DialogHistory.Instance?.RecordLine(dialog.id, line);

                yield return PlayLineAudio(source, line);
                if (!IsPlaying) { RestoreCursor(); ClearActivePortrait(); yield break; }

                // === Конец диалога на этой реплике ===
                if (line.isEnd)
                {
                    break;
                }

                if (line.transitions != null && line.transitions.Count > 0)
                {
                    int chosenIndex = -1;
                    bool chosen = false;

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

                        SetActivePortraitForRole(roleId);

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

                        // === Запись выбранного варианта: очки берём из перехода ===
                        if (recordHistory)
                            DialogHistory.Instance?.RecordLine(
                                dialog.id, choiceLine,
                                isChoice: true,
                                selectedChoiceText: tr.choiceText,
                                targetLineId: tr.targetLineId,
                                parameters: tr.parameters);

                        yield return PlayLineAudio(source, choiceLine);
                        if (!IsPlaying) yield break;
                    }
                    else
                    {
                        // Даже если нет текста/аудио — очки за выбор всё равно должны быть записаны.
                        if (recordHistory)
                            DialogHistory.Instance?.RecordLine(
                                dialog.id,
                                new DialogLine { id = "__choice__", roleId = null, text = string.Empty },
                                isChoice: true,
                                selectedChoiceText: tr.choiceText,
                                targetLineId: tr.targetLineId,
                                parameters: tr.parameters);
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
            ClearActivePortrait();

            OnDialogEnd.Invoke(dialog.id);
            if (speaker != null && !string.IsNullOrEmpty(speaker.OnDialogEndMessage))
                SendScenarioMessage(speaker, speaker.OnDialogEndMessage);

            if (recordHistory) DialogHistory.Instance?.EndDialog();

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

        // ---------- Портрет / камера ----------

        private void SetActivePortraitForRole(string roleId)
        {
            DialogSpeaker speaker = GetSpeakerById(roleId);
            Camera cam = speaker != null ? speaker.SpeakerCamera : null;

            if (cam != null && cam == Camera.main)
            {
                Debug.LogWarning(
                    $"[DialogSystem] Спикер '{roleId}' назначил Camera.main как портретную камеру. " +
                    "Портрет будет скрыт, основная камера не изменяется. " +
                    "Назначьте DialogSpeaker.speakerCamera отдельную камеру.");
                cam = null;
            }

            if (cam == _swappedCamera) return;

            if (_swappedCamera != null)
                _swappedCamera.targetTexture = _swappedCameraOriginalTarget;

            _swappedCamera = cam;
            _swappedCameraOriginalTarget = null;

            if (cam != null && dialogRenderTexture != null)
            {
                _swappedCameraOriginalTarget = cam.targetTexture;
                cam.targetTexture = dialogRenderTexture;
                ui?.SetRenderTexture(dialogRenderTexture);
            }
            else
            {
                ui?.SetRenderTexture(null);
            }
        }

        private void ClearActivePortrait()
        {
            if (_swappedCamera != null)
            {
                _swappedCamera.targetTexture = _swappedCameraOriginalTarget;
                _swappedCamera = null;
                _swappedCameraOriginalTarget = null;
            }

            ui?.SetRenderTexture(null);
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