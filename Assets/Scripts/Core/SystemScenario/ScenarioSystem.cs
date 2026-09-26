using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Game
{
    public class ScenarioSystem : MonoBehaviour
    {
        public static ScenarioSystem Instance { get; private set; }

        [Header("ScenarioSystemSettings")]
        [SerializeField] private List<Scenario> _scenarios = new List<Scenario>();
        [ReadOnly][SerializeField] private Scenario _currentScenario;

        public AudioSource Source;
        [field: SerializeField] public bool IsDebug { get; private set; }

        [Header("Events")]
        public UnityEvent<Scenario> OnStartScenario = new UnityEvent<Scenario>();
        public UnityEvent<Scenario> OnEndScenario = new UnityEvent<Scenario>();
        public UnityEvent<ScenarioStage> OnStartStage = new UnityEvent<ScenarioStage>();
        public UnityEvent<ScenarioStage> OnEndStage = new UnityEvent<ScenarioStage>();
        public UnityEvent<ScenarioStageTransition> OnTransition = new UnityEvent<ScenarioStageTransition>();

        private List<ScenarioUnit> _scenarioUnits;
        private ScenarioStage _currentStage;

        public Scenario CurrentScenario => _currentScenario;
        public ScenarioStage CurrentStage => _currentStage;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _scenarioUnits = new List<ScenarioUnit>(FindObjectsOfType<ScenarioUnit>());
        }

        private void OnEnable()
        {
            if (_scenarios == null || _scenarios.Count == 0) return;

            if (_currentScenario == null)
                _currentScenario = _scenarios[0];

            if (_currentStage == null && _currentScenario.Stages.Count > 0)
                _currentStage = _currentScenario.Stages[0];

            StartScenario();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }


        // ---------- Публичное API ----------

        public void PlayAudio(AudioClip clip)
        {
            StartCoroutine(WaitAudio(clip));
        }

        public void StartScenario()
        {
            if (_currentScenario == null) return;

            if (_currentStage == null && _currentScenario.Stages.Count > 0)
                _currentStage = _currentScenario.Stages[0];

            // Сценарий начался
            OnStartScenario.Invoke(_currentScenario);
            DispatchMessages(_currentScenario.OnStartScenarioMessages);

            // Стартовый этап
            if (_currentStage != null)
            {
                OnStartStage.Invoke(_currentStage);
                DispatchMessages(_currentStage.onStartStageMessages);

                if (_currentStage.audioClip != null)
                    PlayAudio(_currentStage.audioClip);
            }
        }

        public void EndScenario()
        {
            if (_currentScenario == null) return;

            if (_currentStage != null)
            {
                OnEndStage.Invoke(_currentStage);
                DispatchMessages(_currentStage.onEndStageMessages);
            }

            OnEndScenario.Invoke(_currentScenario);
            DispatchMessages(_currentScenario.OnEndScenarioMessages);

            _currentScenario = null;
            _currentStage = null;
        }

        /// <summary>Пришло сообщение от ScenarioUnit — прокидываем в текущий этап сценария.</summary>
        public void RecieveMessage(ScenarioMessage message)
        {
            if (IsDebug) Debug.Log(message.scenarioUnitName + " -> " + message.scenarioUnitMessage);

            if (_currentScenario == null || _currentStage == null) return;

            ScenarioStageTransition transition = _currentStage.RecieveMessage(message);
            if (transition == null) return;

            if (IsDebug && !string.IsNullOrEmpty(transition.ScenarioNextStage))
                Debug.Log("Scenario -> next stage: " + transition.ScenarioNextStage);

            // Событие перехода + сообщения
            OnTransition.Invoke(transition);
            DispatchMessages(transition.onTransitMessages);

            ScenarioStage nextStage = _currentScenario.FindStage(transition.ScenarioNextStage);
            if (nextStage != null)
                TransitionTo(nextStage);
        }

        /// <summary>Отправить сообщение конкретному ScenarioUnit по имени.</summary>
        public void SendMessageToUnit(string unitName, string message)
        {
            if (_scenarioUnits == null) return;

            var unit = _scenarioUnits.Find(u => u != null && u.ScenarioUnitName == unitName);
            unit?.ReceiveMessage(message);
        }


        // ---------- Внутреннее ----------

        private void TransitionTo(ScenarioStage nextStage)
        {
            if (_currentStage != null)
            {
                OnEndStage.Invoke(_currentStage);
                DispatchMessages(_currentStage.onEndStageMessages);
            }

            _currentStage = nextStage;

            OnStartStage.Invoke(_currentStage);
            DispatchMessages(_currentStage.onStartStageMessages);

            if (_currentStage.audioClip != null)
                PlayAudio(_currentStage.audioClip);
        }

        private void DispatchMessages(IReadOnlyList<ScenarioUnitMessage> messages)
        {
            if (messages == null) return;

            for (int i = 0; i < messages.Count; i++)
            {
                var m = messages[i];
                if (m == null) continue;

                SendMessageToUnit(m.ScenarioUnitName, m.ScenarioMessage);
            }
        }

        private IEnumerator WaitAudio(AudioClip clip)
        {
            Source.clip = clip;
            Source.Play();
            yield return new WaitForSeconds(clip.length);

            var msg = new ScenarioMessage("ScenarioSystem", "EndAudio");
            RecieveMessage(msg);
        }
    }
}