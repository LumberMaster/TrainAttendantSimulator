using System.Collections;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// Связующее звено: слушает окончание сценария и автоматически
    /// отправляет результат на фронтенд через GameBridge, после чего
    /// (опционально) выходит в меню выбора сценария.
    ///
    /// • passenger_loyality  ← DialogHistory.Parameters["LoalityPassager"]
    /// • security_rating     ← DialogHistory.Parameters["SecurityRating"]
    /// • result_json         ← JSON-снапшот DialogHistory
    ///
    /// Если на момент окончания сценария GameBridge ещё не получил
    /// OnScenarioStarted (IsRunning == false) — payload буферизуется
    /// и отправляется автоматически, как только придёт ScenarioStarted.
    /// </summary>
    [DisallowMultipleComponent]
    public class ResultSender : MonoBehaviour
    {
        [Header("Источники (можно оставить пустыми — возьмётся .Instance)")]
        [SerializeField] private ScenarioSystem scenarioSystem;
        [SerializeField] private DialogHistory dialogHistory;
        [SerializeField] private GameBridge gameBridge;

        [Header("Имена параметров в DialogHistory")]
        [SerializeField] private string loyalityParameterName = "LoalityPassager";
        [SerializeField] private string securityParameterName = "SecurityRating";

        [Header("Опции отправки")]
        [Tooltip("Округлять очки до целых перед отправкой.")]
        [SerializeField] private bool roundToInt = true;

        [Tooltip("Отправлять результат сразу, даже если GameBridge.IsRunning == false.\n" +
                 "Полезно, когда фронт вообще не присылает OnScenarioStarted.")]
        [SerializeField] private bool sendImmediately = false;

        [Tooltip("Сколько секунд ждать GameBridge.ScenarioStarted, прежде чем отправить принудительно.")]
        [SerializeField] private float waitForBridgeSeconds = 5f;

        [Header("Автовыход")]
        [Tooltip("После отправки результата автоматически выйти в меню выбора сценария.")]
        [SerializeField] private bool exitAfterFinish = true;

        [Tooltip("Кого ждать для выхода:\n" +
                 "• ByDelay  — просто подождать exitDelay секунд и вызвать ExitToMenu();\n" +
                 "• OnResultSaved — дождаться события GameBridge.ResultSaved (сервер подтвердил).")]
        [SerializeField] private ExitMode exitMode = ExitMode.OnResultSaved;

        [Tooltip("Задержка перед выходом (сек). Используется только в режиме ByDelay,\n" +
                 "либо как fallback, если ResultSaved не пришёл за exitFallbackSeconds.")]
        [SerializeField] private float exitDelay = 0.5f;

        [Tooltip("Максимальное время ожидания ResultSaved в режиме OnResultSaved, сек.\n" +
                 "По истечении — выход принудительно (чтобы игрок не залип в игре).")]
        [SerializeField] private float exitFallbackSeconds = 5f;

        [Header("Debug")]
        [SerializeField] private bool verbose = true;

        public enum ExitMode
        {
            ByDelay,
            OnResultSaved
        }

        // -------- State --------
        private bool _sent;
        private bool _subscribedToScenarioSystem;
        private bool _subscribedToBridge;
        private bool _exiting;

        private PendingPayload _pending;
        private float _pendingSince;

        private class PendingPayload
        {
            public int loyality;
            public int security;
            public string resultJson;
        }

        // ------------------------------------------------------------------
        //  Lifecycle
        // ------------------------------------------------------------------

        private void Awake()
        {
            if (scenarioSystem == null) scenarioSystem = ScenarioSystem.Instance;
            if (dialogHistory == null) dialogHistory = DialogHistory.Instance;
            if (gameBridge == null) gameBridge = GameBridge.Instance;
        }

        private void OnEnable()
        {
            TrySubscribeScenarioSystem();
            TrySubscribeBridge();
        }

        private void Start()
        {
            // Instance'ы могли появиться после Awake — повторная попытка.
            TrySubscribeScenarioSystem();
            TrySubscribeBridge();
        }

        private void OnDisable()
        {
            UnsubscribeAll();
        }

        private void Update()
        {
            // Страховка №1: фронт так и не прислал OnScenarioStarted — не ждём вечно.
            if (_pending != null && Time.unscaledTime - _pendingSince > waitForBridgeSeconds)
            {
                Debug.LogWarning(
                    "[ResultSender] ScenarioStarted не пришёл за " +
                    waitForBridgeSeconds + "с — отправляю результат принудительно.");
                FlushPending(force: true);
            }
        }

        // ------------------------------------------------------------------
        //  Подписки
        // ------------------------------------------------------------------

        private void TrySubscribeScenarioSystem()
        {
            if (_subscribedToScenarioSystem) return;
            if (scenarioSystem == null) scenarioSystem = ScenarioSystem.Instance;
            if (scenarioSystem == null) return;

            scenarioSystem.OnEndScenario.AddListener(HandleScenarioEnded);
            _subscribedToScenarioSystem = true;

            if (verbose)
                Debug.Log("[ResultSender] Подписан на ScenarioSystem.OnEndScenario");
        }

        private void TrySubscribeBridge()
        {
            if (_subscribedToBridge) return;
            if (gameBridge == null) gameBridge = GameBridge.Instance;
            if (gameBridge == null) return;

            gameBridge.ScenarioStarted += HandleBridgeScenarioStarted;
            gameBridge.ResultSaved += HandleBridgeResultSaved;
            _subscribedToBridge = true;

            if (verbose)
                Debug.Log("[ResultSender] Подписан на GameBridge.ScenarioStarted / ResultSaved");
        }

        private void UnsubscribeAll()
        {
            if (_subscribedToScenarioSystem && scenarioSystem != null)
                scenarioSystem.OnEndScenario.RemoveListener(HandleScenarioEnded);
            _subscribedToScenarioSystem = false;

            if (_subscribedToBridge && gameBridge != null)
            {
                gameBridge.ScenarioStarted -= HandleBridgeScenarioStarted;
                gameBridge.ResultSaved -= HandleBridgeResultSaved;
            }
            _subscribedToBridge = false;
        }

        // ------------------------------------------------------------------
        //  Обработчик окончания сценария
        // ------------------------------------------------------------------

        private void HandleScenarioEnded(Scenario scenario)
        {
            if (_sent)
            {
                if (verbose) Debug.Log("[ResultSender] Результат уже отправлен — игнор.");
                return;
            }

            if (gameBridge == null) gameBridge = GameBridge.Instance;
            if (dialogHistory == null) dialogHistory = DialogHistory.Instance;

            // 1. Закрыть активный диалог, чтобы в снапшот попал endedAtUtc.
            dialogHistory?.EndDialog();

            // 2. Собрать payload.
            string resultJson = dialogHistory != null
                ? dialogHistory.ExportToJson(prettyPrint: false)
                : null;

            float loyality = dialogHistory != null
                ? dialogHistory.GetParameter(loyalityParameterName)
                : 0f;

            float security = dialogHistory != null
                ? dialogHistory.GetParameter(securityParameterName)
                : 0f;

            int loyalityInt = roundToInt ? Mathf.RoundToInt(loyality) : (int)loyality;
            int securityInt = roundToInt ? Mathf.RoundToInt(security) : (int)security;

            if (verbose)
            {
                Debug.Log(
                    $"[ResultSender] Сценарий '{scenario?.ScenarioName}' завершён.\n" +
                    $"  {loyalityParameterName} = {loyality} → {loyalityInt}\n" +
                    $"  {securityParameterName} = {security} → {securityInt}\n" +
                    $"  result_json length = {(resultJson?.Length ?? 0)}");
            }

            var payload = new PendingPayload
            {
                loyality = loyalityInt,
                security = securityInt,
                resultJson = resultJson
            };

            // 3. Отправить сразу или буферизовать.
            if (gameBridge != null && (gameBridge.IsRunning || sendImmediately))
            {
                SendNow(payload);
            }
            else
            {
                _pending = payload;
                _pendingSince = Time.unscaledTime;

                if (verbose)
                    Debug.Log("[ResultSender] GameBridge ещё не IsRunning — payload буферизован, " +
                              "ждём GameBridge.ScenarioStarted…");

                TrySubscribeBridge();
            }
        }

        // ------------------------------------------------------------------
        //  Обработчики GameBridge
        // ------------------------------------------------------------------

        private void HandleBridgeScenarioStarted(ScenarioStartPayload _)
        {
            if (_pending == null) return;

            if (verbose)
                Debug.Log("[ResultSender] GameBridge.ScenarioStarted получен — отправляю буфер.");

            FlushPending(force: false);
        }

        private void HandleBridgeResultSaved(string json)
        {
            if (!exitAfterFinish) return;
            if (!_sent) return;            // результат ещё не отправляли — нечего закрывать
            if (_exiting) return;

            if (exitMode != ExitMode.OnResultSaved) return;

            if (verbose)
                Debug.Log("[ResultSender] ResultSaved получен — выходим в меню.");

            DoExit();
        }

        // ------------------------------------------------------------------
        //  Отправка
        // ------------------------------------------------------------------

        private void FlushPending(bool force)
        {
            if (_pending == null) return;
            if (gameBridge == null) gameBridge = GameBridge.Instance;
            if (gameBridge == null)
            {
                Debug.LogError("[ResultSender] GameBridge не найден — отправить нельзя.");
                return;
            }

            var p = _pending;
            _pending = null;

            if (!gameBridge.IsRunning && !force && !sendImmediately)
            {
                Debug.LogWarning("[ResultSender] FlushPending: IsRunning == false, пропуск.");
                return;
            }

            SendNow(p);
        }

        private void SendNow(PendingPayload p)
        {
            if (_sent) return;

            if (gameBridge == null) gameBridge = GameBridge.Instance;
            if (gameBridge == null)
            {
                Debug.LogError("[ResultSender] GameBridge не найден — результат не отправлен.");
                return;
            }

            gameBridge.FinishScenario(
                passengerLoyality: p.loyality,
                securityRating: p.security,
                resultJson: p.resultJson);

            _sent = true;

            if (verbose)
                Debug.Log("[ResultSender] FinishScenario отправлен.");

            if (exitAfterFinish)
                StartCoroutine(ExitRoutine());
        }

        // ------------------------------------------------------------------
        //  Автовыход
        // ------------------------------------------------------------------

        private IEnumerator ExitRoutine()
        {
            if (exitMode == ExitMode.ByDelay)
            {
                yield return new WaitForSecondsRealtime(exitDelay);
                DoExit();
                yield break;
            }

            // OnResultSaved: ждём событие ResultSaved максимум exitFallbackSeconds.
            float deadline = Time.unscaledTime + exitFallbackSeconds;
            while (!_exiting && Time.unscaledTime < deadline)
                yield return null;

            if (!_exiting)
            {
                if (verbose)
                    Debug.LogWarning(
                        "[ResultSender] ResultSaved не пришёл за " +
                        exitFallbackSeconds + "с — выходим принудительно.");
                DoExit();
            }
        }

        private void DoExit()
        {
            if (_exiting) return;
            _exiting = true;

            if (gameBridge == null) gameBridge = GameBridge.Instance;
            if (gameBridge == null)
            {
                Debug.LogError("[ResultSender] GameBridge не найден — ExitToMenu невозможен.");
                return;
            }

            if (verbose)
                Debug.Log("[ResultSender] GameBridge.ExitToMenu()");

            gameBridge.ExitToMenu();
        }

        // ------------------------------------------------------------------
        //  Публичный API
        // ------------------------------------------------------------------

        /// <summary>Разрешить повторную отправку (при старте нового сценария).</summary>
        public void ResetSentFlag()
        {
            _sent = false;
            _exiting = false;
            _pending = null;
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(loyalityParameterName))
                loyalityParameterName = "LoalityPassager";
            if (string.IsNullOrWhiteSpace(securityParameterName))
                securityParameterName = "SecurityRating";
        }
    }
}