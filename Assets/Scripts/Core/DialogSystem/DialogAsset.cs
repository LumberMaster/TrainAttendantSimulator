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
        private const string KnownRolesPrefsKey = "DialogAsset.KnownRoles";

        // Поля редактора — сохраняются между перерисовками инспектора.
        private string _currentFromRole = string.Empty;
        private string _replaceToRole = string.Empty;
        private bool _replaceInTransitions = true;
        private string _newKnownRole = string.Empty;

        public override void OnInspectorGUI()
        {
            var dialog = (DialogAsset)target;

            DrawRoleReplaceSection(dialog);

            EditorGUILayout.Space();
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

        // =====================================================================
        //  Replace NPC role
        // =====================================================================

        private static List<string> CollectRoleIds(DialogAsset dialog)
        {
            var set = new HashSet<string>();

            // 1) Роли, уже встречающиеся в диалоге.
            if (dialog != null && dialog.lines != null)
            {
                foreach (var l in dialog.lines)
                {
                    if (l == null) continue;
                    if (!string.IsNullOrEmpty(l.roleId)) set.Add(l.roleId);

                    if (l.transitions != null)
                    {
                        foreach (var t in l.transitions)
                        {
                            if (t == null) continue;
                            if (!string.IsNullOrEmpty(t.speakerRoleId)) set.Add(t.speakerRoleId);
                        }
                    }
                }
            }

            // 2) Роли, добавленные пользователем вручную (общий список в EditorPrefs).
            foreach (var r in LoadKnownRoles())
                if (!string.IsNullOrEmpty(r)) set.Add(r);

            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static List<string> LoadKnownRoles()
        {
            string raw = EditorPrefs.GetString(KnownRolesPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return new List<string>();

            var result = new List<string>();
            foreach (var s in raw.Split('\n'))
            {
                var v = s.Trim();
                if (v.Length > 0) result.Add(v);
            }
            return result;
        }

        private static void SaveKnownRoles(List<string> roles)
        {
            EditorPrefs.SetString(KnownRolesPrefsKey, string.Join("\n", roles));
        }

        private void DrawRoleReplaceSection(DialogAsset dialog)
        {
            EditorGUILayout.LabelField("Replace NPC role", EditorStyles.boldLabel);

            var roles = CollectRoleIds(dialog);
            if (roles.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "В диалоге ещё нет ни одной роли (roleId / speakerRoleId). " +
                    "Добавь роль вручную ниже или заполни реплики.",
                    MessageType.Info);
            }

            // --- Список "из кого" ---
            if (roles.Count > 0)
            {
                int currentIdx = Mathf.Max(0, roles.IndexOf(_currentFromRole));
                currentIdx = EditorGUILayout.Popup("From role", currentIdx, roles.ToArray());
                _currentFromRole = roles[currentIdx];
            }
            else
            {
                _currentFromRole = EditorGUILayout.TextField("From role", _currentFromRole);
            }

            _replaceToRole = EditorGUILayout.TextField("To role", _replaceToRole);
            _replaceInTransitions = EditorGUILayout.Toggle("Also in transitions", _replaceInTransitions);

            bool canApply = !string.IsNullOrEmpty(_currentFromRole)
                            && !string.IsNullOrEmpty(_replaceToRole)
                            && _currentFromRole != _replaceToRole;

            using (new EditorGUI.DisabledScope(!canApply))
            {
                if (GUILayout.Button("Replace in all lines"))
                {
                    int changed = ReplaceRole(dialog, _currentFromRole, _replaceToRole, _replaceInTransitions);
                    Debug.Log($"[DialogAsset] Replaced '{_currentFromRole}' -> '{_replaceToRole}' " +
                              $"({changed} occurrence(s)).");
                }
            }

            if (!string.IsNullOrEmpty(_replaceToRole) && _replaceToRole == _currentFromRole)
            {
                EditorGUILayout.HelpBox("Новое имя роли совпадает со старым.", MessageType.Warning);
            }

            // --- Реестр известных NPC (чтобы ими можно было заменять) ---
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Known NPC roles (editor-wide)", EditorStyles.miniBoldLabel);

            var known = LoadKnownRoles();
            if (known.Count > 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (int i = 0; i < known.Count; i++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(known[i]);
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("→ To role", GUILayout.Width(80)))
                            {
                                _replaceToRole = known[i];
                            }
                            if (GUILayout.Button("✕", GUILayout.Width(24)))
                            {
                                known.RemoveAt(i);
                                SaveKnownRoles(known);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _newKnownRole = EditorGUILayout.TextField(_newKnownRole);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_newKnownRole)))
                {
                    if (GUILayout.Button("Add role", GUILayout.Width(80)))
                    {
                        var list = LoadKnownRoles();
                        var trimmed = _newKnownRole.Trim();
                        if (!list.Contains(trimmed))
                        {
                            list.Add(trimmed);
                            SaveKnownRoles(list);
                        }
                        _newKnownRole = string.Empty;
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private static int ReplaceRole(DialogAsset dialog, string fromRole, string toRole, bool includeTransitions)
        {
            if (string.IsNullOrEmpty(fromRole) || string.IsNullOrEmpty(toRole) || fromRole == toRole)
                return 0;

            Undo.RecordObject(dialog, "Replace dialog role");
            int changed = 0;

            if (dialog.lines != null)
            {
                foreach (var l in dialog.lines)
                {
                    if (l == null) continue;

                    if (l.roleId == fromRole)
                    {
                        l.roleId = toRole;
                        changed++;
                    }

                    if (includeTransitions && l.transitions != null)
                    {
                        foreach (var t in l.transitions)
                        {
                            if (t == null) continue;
                            if (t.speakerRoleId == fromRole)
                            {
                                t.speakerRoleId = toRole;
                                changed++;
                            }
                        }
                    }
                }
            }

            if (changed > 0)
            {
                EditorUtility.SetDirty(dialog);
                AssetDatabase.SaveAssets();
            }

            return changed;
        }

        // =====================================================================
        //  JSON file io
        // =====================================================================

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