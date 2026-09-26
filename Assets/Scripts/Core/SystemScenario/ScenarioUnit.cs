using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Game
{
    public class ScenarioUnit : MonoBehaviour
    {
        [SerializeField] private string scenarioUnitName;
        [SerializeField] private List<ScenarioMessageHandler> messageHandlers = new List<ScenarioMessageHandler>();

        public string ScenarioUnitName => scenarioUnitName;

        /// <summary>Вызывается при ЛЮБОМ полученном из сценария сообщении (до обработчиков).</summary>
        public event Action<string> OnMessageReceived;

        public void SendMessageToScenario(string message)
        {
            var scenarioMessage = new ScenarioMessage(scenarioUnitName, message);
            ScenarioSystem.Instance.RecieveMessage(scenarioMessage);
        }

        public void ReceiveMessage(string message)
        {
            // Сначала оповещаем слушателей (DialogSystem и т.п.)
            OnMessageReceived?.Invoke(message);

            // Затем — UnityEvent-обработчики из инспектора
            foreach (var handler in messageHandlers)
            {
                if (handler.Message == message)
                    handler.OnReceive.Invoke();
            }
        }

        [Serializable]
        public class ScenarioMessageHandler
        {
            public string Message;
            public UnityEvent OnReceive = new UnityEvent();
        }
    }

    [Serializable]
    public class ScenarioMessage
    {
        public string scenarioUnitName;
        public string scenarioUnitMessage;

        public ScenarioMessage(string scenarioUnitName, string scenarioUnitMessage)
        {
            this.scenarioUnitName = scenarioUnitName;
            this.scenarioUnitMessage = scenarioUnitMessage;
        }
    }
}