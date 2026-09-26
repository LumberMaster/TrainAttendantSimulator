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

        // [NEW] Блокировать игрока и показывать курсор на всё время диалога
        [Header("Player Block")]
        [Tooltip("Во время диалога блокировать движение/вращение игрока и показывать курсор.")]
        [SerializeField] private bool blockPlayerDuringDialog = true;

        [Header("Portrait Render")]
        [Tooltip("RenderTexture, в которую рендерит камера текущего говорящего.")]
        [SerializeField] private RenderTexture dialogRenderTexture;

        [Header("History")]
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
        private bool _nextRequested;

        private bool _waitingForNext;
        private int _nextWaitFrames;

        // [CHANGED] Состояние курсора/ввода игрока до старта диалога
        private bool _dialogInputStateSaved;
        private bool _savedCursorVisible;
        private CursorLockMode _savedCursorLockMode;
        private bool _savedPlayerInputLocked;
        private PlayerController _player;

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

            if (ui != null)
            {
                ui.NextRequested += HandleNextRequested;
            }

            // [NEW] Кэшируем игрока, чтобы не искать каждый раз
            _player = FindObjectOfType<PlayerController>(true);
        }

        private void OnEnable()
        {
            if (autoSubscribeToUnits) SubscribeToAllUnits();
        }

        private void OnDisable() => UnsubscribeFromAllUnits();

        private void OnDestroy()
        {
            if (ui != null)
            {
                ui.NextRequested -= HandleNextRequested;
            }

            EndDialogCursorAndInput(); // [CHANGED] было RestoreCursor()
            ClearActivePortrait();
            if (Instance == this) Instance = null;
        }

        private void HandleNextRequested()
        {
            if (!_waitingForNext) return;
            if (_nextWaitFrames < 2) return;
            _nextRequested = true;
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
            ui?.SetNextButtonVisible(false);

            EndDialogCursorAndInput(); // [CHANGED] было RestoreCursor()
            ClearActivePortrait();

            if (IsPlaying)
            {
                var id = CurrentDialog != null ? CurrentDialog.id : string.Empty;
                OnDialogEnd.Invoke(id);

                if (CurrentSpeaker != null && !string.IsNullOrEmpty(CurrentSpeaker.OnDialogEndMessage))
                    SendScenarioMessage(CurrentSpeaker, CurrentSpeaker.OnDialogEndMessage);

                if (recordHistory) DialogHistory.Instance?.EndDialog();
            }

            ResetState();
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

        private void ResetState()
        {
            IsPlaying = false;
            CurrentSpeaker = null;
            CurrentDialog = null;
            _routine = null;
            _skipRequested = false;
            _nextRequested = false;
            _waitingForNext = false;
            _nextWaitFrames = 0;
        }

        private IEnumerator PlayRoutine(DialogSpeaker speaker, DialogAsset dialog)
        {
            IsPlaying = true;
            CurrentSpeaker = speaker;
            CurrentDialog = dialog;
            _skipRequested = false;
            _nextRequested = false;
            _waitingForNext = false;
            _nextWaitFrames = 0;

            // [NEW] Показываем курсор и блокируем игрока на всё время диалога
            BeginDialogCursorAndInput();

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

                SetActivePortraitForRole(line.roleId);

                var role = database.GetRole(line.roleId);

                ui?.SetLine(role, line);
                OnDialogLine.Invoke(role, line);

                if (recordHistory)
                    DialogHistory.Instance?.RecordLine(dialog.id, line);

                bool hasAudio = source != null && line.audio != null;
                if (hasAudio)
                {
                    source.clip = line.audio;
                    source.Play();
                }

                yield return WaitForNextButton();

                if (!IsPlaying)
                {
                    StopAudio(source, hasAudio);
                    EndDialogCursorAndInput(); // [CHANGED]
                    ClearActivePortrait();
                    yield break;
                }

                if (line.isEnd)
                {
                    StopAudio(source, hasAudio);
                    break;
                }

                bool hasChoices = line.transitions != null && line.transitions.Count > 0;

                if (hasChoices)
                {
                    yield return HandleChoicesForLine(line, dialog, source, hasAudio);
                    if (!IsPlaying) yield break;

                    int lastChoice = _lastChosenTargetIndex;
                    _lastChosenTargetIndex = -1;

                    if (lastChoice >= 0) index = lastChoice;
                    else index++;
                }
                else
                {
                    StopAudio(source, hasAudio);
                    index++;
                }
            }

            ui?.Hide();
            ui?.HideChoices();
            ui?.SetTimerActive(false, 0f);
            ui?.SetNextButtonVisible(false);

            EndDialogCursorAndInput(); // [CHANGED]
            ClearActivePortrait();

            OnDialogEnd.Invoke(dialog.id);
            if (speaker != null && !string.IsNullOrEmpty(speaker.OnDialogEndMessage))
                SendScenarioMessage(speaker, speaker.OnDialogEndMessage);

            if (recordHistory) DialogHistory.Instance?.EndDialog();

            ResetState();
        }

        /// <summary>
        /// Показывает кнопку «Далее» и ждёт клик по ней.
        /// Фаза 1: если идёт печать — первый клик мгновенно её завершает (скип).
        /// Фаза 2: второй клик — переход дальше.
        /// </summary>
        private IEnumerator WaitForNextButton()
        {
            yield return null;

            _nextRequested = false;
            _nextWaitFrames = 0;
            _waitingForNext = true;

            ui?.SetNextButtonVisible(true);

            // --- Фаза 1: скип печати ---
            while (IsPlaying && ui != null && ui.IsTyping &&
                   !_nextRequested && !_skipRequested)
            {
                _nextWaitFrames++;
                yield return null;
            }

            if (!IsPlaying)
            {
                _waitingForNext = false;
                _nextWaitFrames = 0;
                _nextRequested = false;
                _skipRequested = false;
                ui?.SetNextButtonVisible(false);
                yield break;
            }

            if (ui != null && ui.IsTyping && (_nextRequested || _skipRequested))
            {
                ui.CompleteTyping();
                _nextRequested = false;
                _skipRequested = false;
                _nextWaitFrames = 0;
            }

            // --- Фаза 2: ожидание клика для перехода ---
            _nextRequested = false;
            _nextWaitFrames = 0;

            while (IsPlaying && !_nextRequested)
            {
                _nextWaitFrames++;
                yield return null;
            }

            _waitingForNext = false;
            _nextWaitFrames = 0;
            _nextRequested = false;
            _skipRequested = false;

            ui?.SetNextButtonVisible(false);
        }

        // ==== Choices ====

        private int _lastChosenTargetIndex = -1;

        private IEnumerator HandleChoicesForLine(
            DialogLine line, DialogAsset dialog, AudioSource source, bool hasAudio)
        {
            int chosenIndex = -1;
            bool chosen = false;

            // [CHANGED] Курсор уже виден (показывается на старте диалога) — здесь ничего не делаем.

            ui?.ShowChoices(line.transitions, i => { chosenIndex = i; chosen = true; });
            ui?.SetTimerActive(line.useTimer, line.timerDuration);

            float timeLeft = line.timerDuration;

            while (IsPlaying && !chosen)
            {
                if (line.useTimer)
                {
                    timeLeft -= Time.deltaTime;
                    ui?.SetTimer(timeLeft);
                    if (timeLeft <= 0f) { chosenIndex = 0; break; }
                }
                yield return null;
            }

            ui?.HideChoices();
            ui?.SetTimerActive(false, 0f);

            StopAudio(source, hasAudio);

            if (!IsPlaying) yield break;

            if (chosenIndex < 0 || chosenIndex >= line.transitions.Count)
            {
                _lastChosenTargetIndex = -1;
                yield break;
            }

            var tr = line.transitions[chosenIndex];
            if (tr == null)
            {
                _lastChosenTargetIndex = -1;
                yield break;
            }

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
                    audio = tr.choiceAudio
                };

                var choiceRole = database.GetRole(roleId);

                ui?.SetLine(choiceRole, choiceLine);
                OnDialogLine.Invoke(choiceRole, choiceLine);

                if (recordHistory)
                    DialogHistory.Instance?.RecordLine(
                        dialog.id, choiceLine,
                        isChoice: true,
                        selectedChoiceText: tr.choiceText,
                        targetLineId: tr.targetLineId,
                        parameters: tr.parameters);

                bool choiceHasAudio = source != null && choiceLine.audio != null;
                if (choiceHasAudio)
                {
                    source.clip = choiceLine.audio;
                    source.Play();
                }

                yield return WaitForNextButton();

                StopAudio(source, choiceHasAudio);

                if (!IsPlaying) yield break;
            }
            else
            {
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
                Debug.LogWarning($"[DialogSystem] Transition target '{tr.targetLineId}' " +
                                 $"не найден в диалоге '{dialog.id}'.");
                _lastChosenTargetIndex = -1;
            }
            else
            {
                _lastChosenTargetIndex = targetIdx;
            }
        }

        private static void StopAudio(AudioSource source, bool hasAudio)
        {
            if (hasAudio && source != null && source.isPlaying)
                source.Stop();
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

        // ---------- Курсор и блокировка игрока ----------

        /// <summary>
        /// Показывает курсор, разблокирует его лок и блокирует управление игроком.
        /// Сохраняет предыдущее состояние, чтобы вернуть его после диалога.
        /// </summary>
        private void BeginDialogCursorAndInput()
        {
            if (_dialogInputStateSaved) return;
            _dialogInputStateSaved = true;

            _savedCursorVisible = Cursor.visible;
            _savedCursorLockMode = Cursor.lockState;

            if (!blockPlayerDuringDialog) return;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (_player == null) _player = FindObjectOfType<PlayerController>(true);
            if (_player != null)
            {
                _savedPlayerInputLocked = _player.IsInputLocked;
                _player.LockInput();
            }
        }

        /// <summary>
        /// Возвращает курсор и управление игроком в состояние, которое было до диалога.
        /// </summary>
        private void EndDialogCursorAndInput()
        {
            if (!_dialogInputStateSaved) return;
            _dialogInputStateSaved = false;

            Cursor.visible = _savedCursorVisible;
            Cursor.lockState = _savedCursorLockMode;

            if (_player != null && !_savedPlayerInputLocked)
                _player.UnlockInput();
        }
    }
}