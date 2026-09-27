using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game
{
    [CreateAssetMenu(fileName = "Scenario", menuName = "Game/Scenario")]
    public class Scenario : ScriptableObject
    {
        [SerializeField] private string uuid;
        [SerializeField] private string scenarioName;
        [SerializeField] private string description;
        [SerializeField] private List<ScenarioStage> stages = new List<ScenarioStage>();

        [Header("Messages dispatched on scenario events")]
        [SerializeField] private List<ScenarioUnitMessage> onStartScenarioMessages = new List<ScenarioUnitMessage>();
        [SerializeField] private List<ScenarioUnitMessage> onEndScenarioMessages = new List<ScenarioUnitMessage>();

        public string Uuid => uuid;
        public string ScenarioName => scenarioName;
        public string Description => description;
        public IReadOnlyList<ScenarioStage> Stages => stages;

        public IReadOnlyList<ScenarioUnitMessage> OnStartScenarioMessages => onStartScenarioMessages;
        public IReadOnlyList<ScenarioUnitMessage> OnEndScenarioMessages => onEndScenarioMessages;

        public void SetUuid(string value)
        {
            uuid = value;
        }

        public ScenarioStage FindStage(string stageName)
        {
            foreach (var stage in stages)
                if (stage.stageName == stageName) return stage;

            return null;
        }

        public int IndexOf(ScenarioStage stage) => stages.IndexOf(stage);


#if UNITY_EDITOR
        // =====================================================================
        //  EDITOR-ONLY: UUID / JSON export / import
        // =====================================================================

        public void RegenerateUuid()
        {
            uuid = Guid.NewGuid().ToString();
        }

        /// <summary>Сериализует сценарий в JSON-строку.</summary>
        public string ExportToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(ToJsonData(), prettyPrint);
        }

        /// <summary>Загружает данные сценария из JSON-строки, полностью заменяя текущие.</summary>
        public void ImportFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("JSON is empty.");

            var data = JsonUtility.FromJson<ScenarioJsonData>(json);
            if (data == null)
                throw new Exception("Failed to parse scenario JSON.");

            FromJsonData(data);

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        private ScenarioJsonData ToJsonData()
        {
            var data = new ScenarioJsonData
            {
                uuid = uuid,
                scenarioName = scenarioName,
                description = description,
                onStartScenarioMessages = CloneMessages(onStartScenarioMessages),
                onEndScenarioMessages = CloneMessages(onEndScenarioMessages),
                stages = new List<ScenarioStageJsonData>()
            };

            if (stages != null)
            {
                foreach (var s in stages)
                {
                    if (s == null) continue;

                    var stageData = new ScenarioStageJsonData
                    {
                        stageName = s.stageName,
                        stageNameDisplay = s.stageNameDisplay,
                        description = s.description,
                        isEndStage = s.IsEndStage,
                        audioClipPath = s.audioClip != null ? AssetDatabase.GetAssetPath(s.audioClip) : string.Empty,
                        onStartStageMessages = CloneMessages(s.onStartStageMessages),
                        onEndStageMessages = CloneMessages(s.onEndStageMessages),
                        transitions = new List<ScenarioStageTransitionJsonData>()
                    };

                    if (s.transitions != null)
                    {
                        foreach (var t in s.transitions)
                        {
                            if (t == null) continue;

                            stageData.transitions.Add(new ScenarioStageTransitionJsonData
                            {
                                conditions = CloneConditions(t.conditions),
                                ScenarioNextStage = t.ScenarioNextStage,
                                onTransitMessages = CloneMessages(t.onTransitMessages)
                            });
                        }
                    }

                    data.stages.Add(stageData);
                }
            }

            return data;
        }

        private void FromJsonData(ScenarioJsonData data)
        {
            uuid = string.IsNullOrEmpty(data.uuid) ? Guid.NewGuid().ToString() : data.uuid;
            scenarioName = data.scenarioName;
            description = data.description;

            onStartScenarioMessages = CloneMessages(data.onStartScenarioMessages);
            onEndScenarioMessages = CloneMessages(data.onEndScenarioMessages);

            stages = new List<ScenarioStage>();

            if (data.stages != null)
            {
                foreach (var sd in data.stages)
                {
                    if (sd == null) continue;

                    var stage = new ScenarioStage
                    {
                        stageName = sd.stageName,
                        stageNameDisplay = sd.stageNameDisplay,
                        description = sd.description,
                        IsEndStage = sd.isEndStage,
                        audioClip = string.IsNullOrEmpty(sd.audioClipPath)
                            ? null
                            : AssetDatabase.LoadAssetAtPath<AudioClip>(sd.audioClipPath),
                        onStartStageMessages = CloneMessages(sd.onStartStageMessages),
                        onEndStageMessages = CloneMessages(sd.onEndStageMessages),
                        transitions = new List<ScenarioStageTransition>()
                    };

                    if (sd.transitions != null)
                    {
                        foreach (var td in sd.transitions)
                        {
                            if (td == null) continue;

                            var transition = new ScenarioStageTransition
                            {
                                ScenarioNextStage = td.ScenarioNextStage,
                                onTransitMessages = CloneMessages(td.onTransitMessages),
                                conditions = CloneConditions(td.conditions)
                            };

                            // Миграция со старого формата (одиночное условие).
                            if (transition.conditions.Count == 0
                                && (!string.IsNullOrEmpty(td.ScenarioUnitName)
                                    || !string.IsNullOrEmpty(td.ScenarioMessage)))
                            {
                                transition.conditions.Add(new ScenarioTransitionCondition
                                {
                                    ScenarioUnitName = td.ScenarioUnitName,
                                    ScenarioMessage = td.ScenarioMessage
                                });
                            }

                            stage.transitions.Add(transition);
                        }
                    }

                    stages.Add(stage);
                }
            }
        }

        private static List<ScenarioUnitMessage> CloneMessages(List<ScenarioUnitMessage> source)
        {
            var result = new List<ScenarioUnitMessage>();
            if (source == null) return result;

            foreach (var m in source)
            {
                if (m == null) continue;
                result.Add(new ScenarioUnitMessage
                {
                    ScenarioUnitName = m.ScenarioUnitName,
                    ScenarioMessage = m.ScenarioMessage
                });
            }

            return result;
        }

        private static List<ScenarioTransitionCondition> CloneConditions(
            List<ScenarioTransitionCondition> source)
        {
            var result = new List<ScenarioTransitionCondition>();
            if (source == null) return result;

            foreach (var c in source)
            {
                if (c == null) continue;
                result.Add(new ScenarioTransitionCondition
                {
                    ScenarioUnitName = c.ScenarioUnitName,
                    ScenarioMessage = c.ScenarioMessage
                });
            }

            return result;
        }
#endif
    }


    // =========================================================================
    //  Runtime data
    // =========================================================================

    [Serializable]
    public class ScenarioStage
    {
        [Tooltip("Техническое (уникальное) имя этапа, используется в переходах.")]
        public string stageName;

        [Tooltip("Отображаемое имя этапа (для UI/логов).")]
        public string stageNameDisplay;

        public string description;
        public AudioClip audioClip;

        [Tooltip("Если включено — при входе на этот этап сценарий автоматически завершается.")]
        public bool IsEndStage;

        public List<ScenarioStageTransition> transitions = new List<ScenarioStageTransition>();

        [Header("Messages dispatched on stage events")]
        public List<ScenarioUnitMessage> onStartStageMessages = new List<ScenarioUnitMessage>();
        public List<ScenarioUnitMessage> onEndStageMessages = new List<ScenarioUnitMessage>();

        // Накапливаемые сообщения текущего этапа (для многоусловных переходов).
        // [NonSerialized] — не попадает ни в ScriptableObject-ассет, ни в JSON.
        [NonSerialized] private List<ScenarioMessage> receivedMessages = new List<ScenarioMessage>();

        public IReadOnlyList<ScenarioMessage> ReceivedMessages => receivedMessages;

        /// <summary>Сброс накопленных сообщений. Вызывается при входе на этап.</summary>
        public void ResetReceivedMessages()
        {
            if (receivedMessages == null)
                receivedMessages = new List<ScenarioMessage>();
            else
                receivedMessages.Clear();
        }

        /// <summary>
        /// Принимает сообщение, копит его и проверяет, не выполнены ли
        /// условия какого-либо перехода. Возвращает первый сработавший
        /// переход либо null.
        /// </summary>
        public ScenarioStageTransition RecieveMessage(ScenarioMessage message)
        {
            if (receivedMessages == null)
                receivedMessages = new List<ScenarioMessage>();

            if (message != null)
                receivedMessages.Add(message);

            if (transitions == null) return null;

            foreach (var transition in transitions)
            {
                if (transition != null && transition.Matches(receivedMessages))
                {
                    // Переход сработал — сбрасываем накопленное,
                    // чтобы следующий этап начал «с чистого листа».
                    receivedMessages.Clear();
                    return transition;
                }
            }

            return null;
        }
    }


    /// <summary>Одно условие перехода: конкретный объект должен прислать конкретное сообщение.</summary>
    [Serializable]
    public class ScenarioTransitionCondition
    {
        public string ScenarioUnitName;
        public string ScenarioMessage;

        public bool Matches(ScenarioMessage message)
        {
            if (message == null) return false;
            return message.scenarioUnitName == ScenarioUnitName
                && message.scenarioUnitMessage == ScenarioMessage;
        }
    }


    [Serializable]
    public class ScenarioStageTransition
    {
        [Tooltip("Все условия должны быть выполнены, чтобы сработал переход.")]
        public List<ScenarioTransitionCondition> conditions = new List<ScenarioTransitionCondition>();

        public string ScenarioNextStage;

        [Header("Messages dispatched on transition")]
        public List<ScenarioUnitMessage> onTransitMessages = new List<ScenarioUnitMessage>();

        /// <summary>
        /// Проверяет, что среди уже полученных сообщений есть хотя бы по одному,
        /// удовлетворяющему каждому условию перехода.
        /// </summary>
        public bool Matches(IReadOnlyList<ScenarioMessage> receivedMessages)
        {
            if (conditions == null || conditions.Count == 0) return false;
            if (receivedMessages == null) return false;

            for (int c = 0; c < conditions.Count; c++)
            {
                var cond = conditions[c];
                if (cond == null) continue;

                bool satisfied = false;
                for (int i = 0; i < receivedMessages.Count; i++)
                {
                    if (cond.Matches(receivedMessages[i]))
                    {
                        satisfied = true;
                        break;
                    }
                }

                if (!satisfied) return false;
            }

            return true;
        }
    }


    [Serializable]
    public class ScenarioUnitMessage
    {
        public string ScenarioUnitName;
        public string ScenarioMessage;
    }


    // =========================================================================
    //  JSON DTOs (используются только в редакторе)
    // =========================================================================

    [Serializable]
    public class ScenarioJsonData
    {
        public string uuid;
        public string scenarioName;
        public string description;
        public List<ScenarioStageJsonData> stages = new List<ScenarioStageJsonData>();
        public List<ScenarioUnitMessage> onStartScenarioMessages = new List<ScenarioUnitMessage>();
        public List<ScenarioUnitMessage> onEndScenarioMessages = new List<ScenarioUnitMessage>();
    }

    [Serializable]
    public class ScenarioStageJsonData
    {
        public string stageName;
        public string stageNameDisplay;
        public string description;
        public bool isEndStage;
        public string audioClipPath;
        public List<ScenarioStageTransitionJsonData> transitions = new List<ScenarioStageTransitionJsonData>();
        public List<ScenarioUnitMessage> onStartStageMessages = new List<ScenarioUnitMessage>();
        public List<ScenarioUnitMessage> onEndStageMessages = new List<ScenarioUnitMessage>();
    }

    [Serializable]
    public class ScenarioStageTransitionJsonData
    {
        public List<ScenarioTransitionCondition> conditions = new List<ScenarioTransitionCondition>();
        public string ScenarioNextStage;
        public List<ScenarioUnitMessage> onTransitMessages = new List<ScenarioUnitMessage>();

        // LEGACY: поддержка старых JSON с одиночным условием.
        public string ScenarioUnitName;
        public string ScenarioMessage;
    }


#if UNITY_EDITOR
    // =========================================================================
    //  Custom inspector
    // =========================================================================

    [CustomEditor(typeof(Scenario))]
    public class ScenarioEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var scenario = (Scenario)target;

            // --- UUID (editable) ---
            EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            string newUuid = EditorGUILayout.TextField("UUID", scenario.Uuid);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(scenario, "Change Scenario UUID");
                scenario.SetUuid(newUuid);
                EditorUtility.SetDirty(scenario);
            }

            if (GUILayout.Button("Regenerate UUID"))
            {
                Undo.RecordObject(scenario, "Regenerate Scenario UUID");
                scenario.RegenerateUuid();
                EditorUtility.SetDirty(scenario);
            }

            EditorGUILayout.Space();

            // --- Default fields ---
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("JSON", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export to file..."))
                    ExportToFile(scenario);

                if (GUILayout.Button("Import from file..."))
                    ImportFromFile(scenario);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy JSON"))
                {
                    EditorGUIUtility.systemCopyBuffer = scenario.ExportToJson(true);
                    Debug.Log("[Scenario] JSON copied to clipboard.");
                }

                if (GUILayout.Button("Paste JSON"))
                {
                    string json = EditorGUIUtility.systemCopyBuffer;
                    if (string.IsNullOrEmpty(json))
                    {
                        Debug.LogWarning("[Scenario] Clipboard is empty.");
                        return;
                    }

                    ApplyImport(scenario, json);
                }
            }
        }

        private static void ExportToFile(Scenario scenario)
        {
            string defaultName = string.IsNullOrEmpty(scenario.ScenarioName)
                ? "Scenario"
                : scenario.ScenarioName;

            string path = EditorUtility.SaveFilePanel(
                "Export Scenario to JSON", "", defaultName + ".json", "json");

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                string json = scenario.ExportToJson(true);
                System.IO.File.WriteAllText(path, json);
                AssetDatabase.Refresh();
                Debug.Log($"[Scenario] Exported to: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scenario] Export failed: {e.Message}");
            }
        }

        private static void ImportFromFile(Scenario scenario)
        {
            string path = EditorUtility.OpenFilePanel("Import Scenario from JSON", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                string json = System.IO.File.ReadAllText(path);
                ApplyImport(scenario, json);
                Debug.Log($"[Scenario] Imported from: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scenario] Import failed: {e.Message}");
            }
        }

        private static void ApplyImport(Scenario scenario, string json)
        {
            Undo.RecordObject(scenario, "Import Scenario from JSON");
            scenario.ImportFromJson(json);

            // Пересобрать инспектор, чтобы отобразить новые данные
            GUIUtility.ExitGUI();
        }
    }
#endif
}