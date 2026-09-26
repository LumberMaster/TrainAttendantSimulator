using UnityEngine;

namespace Game
{
    /// <summary>
    /// Вешается на NPC. Держит AudioSource для озвучки реплик,
    /// Camera для «портрета» во время диалога
    /// и предоставляет свои данные DialogSystem.
    /// Имя в UI берётся из БД по roleId реплики, а не отсюда.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class DialogSpeaker : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Идентификатор спикера. Используется UI, чтобы понять, в какой RawImage выводить портрет с камеры. Должен совпадать с id в списке слотов DialogUI.")]
        [SerializeField] private string speakerId = "";

        [Header("References")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private ScenarioUnit scenarioUnit;

        [Tooltip("Камера, вид с которой показывается в UI диалога. Если пусто — берётся Camera с этого же объекта.")]
        [SerializeField] private Camera speakerCamera;

        [Header("Scenario Messages")]
        [Tooltip("Сообщение, отправляемое в ScenarioSystem после завершения диалога.")]
        [SerializeField] private string onDialogEndMessage = "DialogEnd";

        [Tooltip("Если пусто — сообщение о старте диалога не отправляется.")]
        [SerializeField] private string onDialogStartMessage = "DialogStart";

        public string SpeakerId => speakerId;
        public AudioSource AudioSource => audioSource;
        public ScenarioUnit ScenarioUnit => scenarioUnit;
        public Camera SpeakerCamera => speakerCamera;
        public string UnitName => scenarioUnit != null ? scenarioUnit.ScenarioUnitName : string.Empty;
        public string OnDialogEndMessage => onDialogEndMessage;
        public string OnDialogStartMessage => onDialogStartMessage;

        private void Awake()
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (scenarioUnit == null) scenarioUnit = GetComponent<ScenarioUnit>();

            if (speakerCamera == null)
            {
                var cam = GetComponent<Camera>();
                // Не берём основную игровую камеру как портретную.
                if (cam != null && cam != Camera.main)
                    speakerCamera = cam;
            }

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