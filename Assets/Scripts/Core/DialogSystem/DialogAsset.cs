using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game
{
    [CreateAssetMenu(fileName = "Dialog", menuName = "Game/Dialog")]
    public class DialogAsset : ScriptableObject
    {
        [Tooltip("Уникальный строковый id диалога (совпадает с именем сообщения из сценария).")]
        public string id;

        [TextArea(1, 3)] public string description;

        public List<DialogLine> lines = new List<DialogLine>();

        public DialogLine FindLine(string lineId)
        {
            if (string.IsNullOrEmpty(lineId)) return null;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i] != null && lines[i].id == lineId) return lines[i];
            return null;
        }

        public int IndexOfLine(string lineId)
        {
            if (string.IsNullOrEmpty(lineId)) return -1;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i] != null && lines[i].id == lineId) return i;
            return -1;
        }

#if UNITY_EDITOR
        // =====================================================================
        //  EDITOR-ONLY: JSON export / import
        // =====================================================================

        public string ExportToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(ToJsonData(), prettyPrint);
        }

        public void ImportFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("JSON is empty.");

            var data = JsonUtility.FromJson<DialogJsonData>(json);
            if (data == null)
                throw new Exception("Failed to parse dialog JSON.");

            FromJsonData(data);

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        private DialogJsonData ToJsonData()
        {
            var data = new DialogJsonData
            {
                id = id,
                description = description,
                lines = new List<DialogLineJsonData>()
            };

            if (lines != null)
            {
                foreach (var l in lines)
                {
                    if (l == null) continue;

                    var lineData = new DialogLineJsonData
                    {
                        id = l.id,
                        roleId = l.roleId,
                        text = l.text,
                        audioPath = l.audio != null ? AssetDatabase.GetAssetPath(l.audio) : string.Empty,
                        isEnd = l.isEnd,
                        useTimer = l.useTimer,
                        timerDuration = l.timerDuration,
                        transitions = new List<DialogLineTransitionJsonData>()
                    };

                    if (l.transitions != null)
                    {
                        foreach (var t in l.transitions)
                        {
                            if (t == null) continue;

                            var trData = new DialogLineTransitionJsonData
                            {
                                choiceText = t.choiceText,
                                speakerRoleId = t.speakerRoleId,
                                choiceAudioPath = t.choiceAudio != null
                                    ? AssetDatabase.GetAssetPath(t.choiceAudio)
                                    : string.Empty,
                                targetLineId = t.targetLineId,
                                parameters = new List<DialogLineParameterJsonData>()
                            };

                            if (t.parameters != null)
                            {
                                foreach (var p in t.parameters)
                                {
                                    if (p == null) continue;
                                    trData.parameters.Add(new DialogLineParameterJsonData
                                    {
                                        name = p.name,
                                        value = p.value
                                    });
                                }
                            }

                            lineData.transitions.Add(trData);
                        }
                    }

                    data.lines.Add(lineData);
                }
            }

            return data;
        }

        private void FromJsonData(DialogJsonData data)
        {
            id = data.id;
            description = data.description;
            lines = new List<DialogLine>();

            if (data.lines != null)
            {
                foreach (var l in data.lines)
                {
                    if (l == null) continue;

                    var line = new DialogLine
                    {
                        id = l.id,
                        roleId = l.roleId,
                        text = l.text,
                        audio = string.IsNullOrEmpty(l.audioPath)
                            ? null
                            : AssetDatabase.LoadAssetAtPath<AudioClip>(l.audioPath),
                        isEnd = l.isEnd,
                        useTimer = l.useTimer,
                        timerDuration = l.timerDuration,
                        transitions = new List<DialogLineTransition>()
                    };

                    if (l.transitions != null)
                    {
                        foreach (var t in l.transitions)
                        {
                            if (t == null) continue;

                            var tr = new DialogLineTransition
                            {
                                choiceText = t.choiceText,
                                speakerRoleId = t.speakerRoleId,
                                choiceAudio = string.IsNullOrEmpty(t.choiceAudioPath)
                                    ? null
                                    : AssetDatabase.LoadAssetAtPath<AudioClip>(t.choiceAudioPath),
                                targetLineId = t.targetLineId,
                                parameters = new List<DialogLineParameter>()
                            };

                            if (t.parameters != null)
                            {
                                foreach (var p in t.parameters)
                                {
                                    if (p == null) continue;
                                    tr.parameters.Add(new DialogLineParameter
                                    {
                                        name = p.name,
                                        value = p.value
                                    });
                                }
                            }

                            line.transitions.Add(tr);
                        }
                    }

                    lines.Add(line);
                }
            }
        }
#endif
    }

    [Serializable]
    public class DialogLine
    {
        [Tooltip("Уникальный id реплики внутри диалога.")]
        public string id;

        [Tooltip("id роли, которая произносит реплику.")]
        public string roleId;

        [TextArea(2, 6)] public string text;

        public AudioClip audio;

        [Header("Flow")]
        [Tooltip("Если true — диалог завершается на этой реплике. " +
                 "Варианты перехода (transitions) игнорируются.")]
        public bool isEnd;

        [Header("Timer (для срочных вопросов)")]
        public bool useTimer;
        [Tooltip("Длительность таймера (сек). По истечении выберется первый переход.")]
        public float timerDuration = 5f;

        [Header("Transitions / Choices")]
        [Tooltip("Список вариантов ответа. Если пуст — переход к следующей реплике по индексу.")]
        public List<DialogLineTransition> transitions = new List<DialogLineTransition>();
    }

    [Serializable]
    public class DialogLineParameter
    {
        [Tooltip("Имя параметра (например, 'trust', 'anger', 'reputation').")]
        public string name;

        [Tooltip("Сколько очков добавляется к параметру при выборе этого ответа.")]
        public float value;
    }

    [Serializable]
    public class DialogLineTransition
    {
        [Tooltip("Текст на кнопке — то, что игрок отвечает собеседнику.")]
        public string choiceText;

        [Tooltip("Кто произносит выбор (roleId). Если пусто — реплика без имени.")]
        public string speakerRoleId;

        [Tooltip("Опциональное аудио для реплики выбора.")]
        public AudioClip choiceAudio;

        [Tooltip("id следующей реплики в этом же диалоге.")]
        public string targetLineId;

        [Header("Parameters (очки за выбор)")]
        [Tooltip("Список параметров, к которым добавляются очки при выборе этого ответа.")]
        public List<DialogLineParameter> parameters = new List<DialogLineParameter>();
    }

    // =========================================================================
    //  JSON DTOs
    // =========================================================================

    [Serializable]
    public class DialogJsonData
    {
        public string id;
        public string description;
        public List<DialogLineJsonData> lines = new List<DialogLineJsonData>();
    }

    [Serializable]
    public class DialogLineJsonData
    {
        public string id;
        public string roleId;
        public string text;
        public string audioPath;
        public bool isEnd;
        public bool useTimer;
        public float timerDuration = 5f;
        public List<DialogLineTransitionJsonData> transitions = new List<DialogLineTransitionJsonData>();
    }

    [Serializable]
    public class DialogLineParameterJsonData
    {
        public string name;
        public float value;
    }

    [Serializable]
    public class DialogLineTransitionJsonData
    {
        public string choiceText;
        public string speakerRoleId;
        public string choiceAudioPath;
        public string targetLineId;
        public List<DialogLineParameterJsonData> parameters = new List<DialogLineParameterJsonData>();
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(DialogAsset))]
    public class DialogAssetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var dialog = (DialogAsset)target;

            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("JSON", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export to file...")) ExportToFile(dialog);
                if (GUILayout.Button("Import from file...")) ImportFromFile(dialog);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy JSON"))
                {
                    EditorGUIUtility.systemCopyBuffer = dialog.ExportToJson(true);
                    Debug.Log("[DialogAsset] JSON copied to clipboard.");
                }

                if (GUILayout.Button("Paste JSON"))
                {
                    string json = EditorGUIUtility.systemCopyBuffer;
                    if (string.IsNullOrEmpty(json))
                    {
                        Debug.LogWarning("[DialogAsset] Clipboard is empty.");
                        return;
                    }
                    ApplyImport(dialog, json);
                }
            }
        }

        private static void ExportToFile(DialogAsset dialog)
        {
            string defaultName = string.IsNullOrEmpty(dialog.id) ? dialog.name : dialog.id;

            string path = EditorUtility.SaveFilePanel(
                "Export Dialog to JSON", "", defaultName + ".json", "json");

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                System.IO.File.WriteAllText(path, dialog.ExportToJson(true));
                AssetDatabase.Refresh();
                Debug.Log($"[DialogAsset] Exported to: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DialogAsset] Export failed: {e.Message}");
            }
        }

        private static void ImportFromFile(DialogAsset dialog)
        {
            string path = EditorUtility.OpenFilePanel("Import Dialog from JSON", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                string json = System.IO.File.ReadAllText(path);
                ApplyImport(dialog, json);
                Debug.Log($"[DialogAsset] Imported from: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DialogAsset] Import failed: {e.Message}");
            }
        }

        private static void ApplyImport(DialogAsset dialog, string json)
        {
            Undo.RecordObject(dialog, "Import Dialog from JSON");
            dialog.ImportFromJson(json);
            GUIUtility.ExitGUI();
        }
    }
#endif
}