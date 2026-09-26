using UnityEngine;

namespace Game
{
    /// <summary>
    /// Вешается на NPC. Держит AudioSource для озвучки реплик
    /// и предоставляет свои данные DialogSystem.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class DialogSpeaker : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Имя для отображения в UI. Если пусто — берётся displayName роли.")]
        [SerializeField] private string displayName;

        [Tooltip("Опциональный портрет, перекрывает портрет роли.")]
        [SerializeField] private Sprite overridePortrait;

        [Header("References")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private ScenarioUnit scenarioUnit;

        [Header("Scenario Messages")]
        [Tooltip("Сообщение, отправляемое в ScenarioSystem после завершения диалога.")]
        [SerializeField] private string onDialogEndMessage = "DialogEnd";

        [Tooltip("Если пусто — сообщение о старте диалога не отправляется.")]
        [SerializeField] private string onDialogStartMessage = "DialogStart";

        public string DisplayName => displayName;
        public Sprite OverridePortrait => overridePortrait;
        public AudioSource AudioSource => audioSource;
        public ScenarioUnit ScenarioUnit => scenarioUnit;
        public string UnitName => scenarioUnit != null ? scenarioUnit.ScenarioUnitName : string.Empty;
        public string OnDialogEndMessage => onDialogEndMessage;
        public string OnDialogStartMessage => onDialogStartMessage;

        private void Awake()
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (scenarioUnit == null) scenarioUnit = GetComponent<ScenarioUnit>();

            if (scenarioUnit == null)
                Debug.LogWarning($"[DialogSpeaker] На '{name}' нет ScenarioUnit — сообщения из сценария не будут приходить.");
        }

        /// <summary>Ручной запуск диалога по id (не через сообщение сценария).</summary>
        public void PlayDialog(string dialogId)
        {
            if (DialogSystem.Instance == null)
            {
                Debug.LogWarning("[DialogSpeaker] DialogSystem.Instance is null.");
                return;
            }
            DialogSystem.Instance.StartDialog(this, dialogId);
        }

        public void StopDialog() => DialogSystem.Instance?.StopDialog();
    }
}