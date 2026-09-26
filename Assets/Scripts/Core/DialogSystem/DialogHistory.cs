using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game
{
    /// <summary>
    /// Хранит историю всех проигранных диалогов и накапливает параметры (очки)
    /// С НАЧАЛА ТЕКУЩЕГО ЗАПУСКА СИМУЛЯЦИИ.
    /// Единственное место, где считается общий счёт по параметрам.
    /// При появлении нового имени параметра его значение начинается с 0
    /// и увеличивается на value из выбранного перехода (DialogLineTransition.parameters).
    /// Между сессиями сохраняется только если persistAcrossSessions = true.
    /// </summary>
    public class DialogHistory : MonoBehaviour
    {
        public static DialogHistory Instance { get; private set; }

        [Header("Persistence")]
        [Tooltip("Сохранять историю между сессиями. По умолчанию ВЫКЛЮЧЕНО — " +
                 "история и очки накапливаются с начала текущего запуска симуляции " +
                 "и сбрасываются при каждом новом запуске.")]
        [SerializeField] private bool persistAcrossSessions = false;

        [SerializeField] private bool usePlayerPrefs = true;
        [SerializeField] private string playerPrefsKey = "DialogHistory";
        [Tooltip("Используется только если usePlayerPrefs = false.")]
        [SerializeField] private string filePath = "dialog_history.json";

        [Header("Debug")]
        [SerializeField] private bool logRecordedLines = false;

        // ---------- DTO ----------

        [Serializable]
        public class ParameterEntry
        {
            public string name;
            public float value;        // дельта, добавленная этим выбором
            public float totalAfter;   // накопленный итог после применения value
        }

        [Serializable]
        public class LineRecord
        {
            public string dialogId;
            public string lineId;
            public string roleId;
            public string text;
            public bool isChoice;
            public string selectedChoiceText;
            public string targetLineId;
            public string timestampUtc;
            public List<ParameterEntry> parameters = new List<ParameterEntry>();
        }

        [Serializable]
        public class DialogRecord
        {
            public string dialogId;
            public string speakerId;
            public string startedAtUtc;
            public string endedAtUtc;
            public List<LineRecord> lines = new List<LineRecord>();
        }

        [Serializable]
        public class HistorySnapshot
        {
            public string sessionStartedUtc;
            public List<DialogRecord> dialogs = new List<DialogRecord>();
            public List<ParameterEntry> totals = new List<ParameterEntry>();
        }

        // ---------- State ----------

        private readonly List<DialogRecord> _records = new List<DialogRecord>();
        private readonly Dictionary<string, float> _parameters = new Dictionary<string, float>();
        private DialogRecord _currentRecord;
        private string _sessionStartedUtc;

        public IReadOnlyList<DialogRecord> Records => _records;
        public IReadOnlyDictionary<string, float> Parameters => _parameters;
        public string SessionStartedUtc => _sessionStartedUtc;

        public event Action<string, float> OnParameterChanged;   // name, newTotal
        public event Action<LineRecord> OnLineRecorded;
        public event Action<DialogRecord> OnDialogRecorded;

        // ---------- Lifecycle ----------

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _sessionStartedUtc = DateTime.UtcNow.ToString("o");

            if (persistAcrossSessions) Load();

            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (persistAcrossSessions) Save();
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit()
        {
            if (persistAcrossSessions) Save();
        }

        // ---------- Recording API ----------

        public void BeginDialog(string dialogId, string speakerId)
        {
            if (_currentRecord != null) EndDialog();

            _currentRecord = new DialogRecord
            {
                dialogId = dialogId,
                speakerId = speakerId,
                startedAtUtc = DateTime.UtcNow.ToString("o"),
                lines = new List<LineRecord>()
            };
            _records.Add(_currentRecord);
        }

        public void EndDialog()
        {
            if (_currentRecord == null) return;
            _currentRecord.endedAtUtc = DateTime.UtcNow.ToString("o");
            OnDialogRecorded?.Invoke(_currentRecord);
            _currentRecord = null;
        }

        /// <summary>
        /// Записывает реплику в историю.
        /// Обычные реплики вызываются без <paramref name="parameters"/> — очков не начисляют.
        /// Для выбора игрока передавайте <c>transition.parameters</c>: именно там теперь
        /// хранятся очки за конкретный ответ.
        /// </summary>
        public LineRecord RecordLine(
            string dialogId,
            DialogLine line,
            bool isChoice = false,
            string selectedChoiceText = null,
            string targetLineId = null,
            List<DialogLineParameter> parameters = null)
        {
            if (line == null) return null;

            var record = new LineRecord
            {
                dialogId = dialogId,
                lineId = line.id,
                roleId = line.roleId,
                text = line.text,
                isChoice = isChoice,
                selectedChoiceText = selectedChoiceText,
                targetLineId = targetLineId,
                timestampUtc = DateTime.UtcNow.ToString("o")
            };

            if (parameters != null)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    var p = parameters[i];
                    if (p == null || string.IsNullOrEmpty(p.name)) continue;

                    // Новое имя параметра => GetParameter вернёт 0.
                    float current = GetParameter(p.name) + p.value;
                    _parameters[p.name] = current;

                    record.parameters.Add(new ParameterEntry
                    {
                        name = p.name,
                        value = p.value,
                        totalAfter = current
                    });

                    if (logRecordedLines)
                        Debug.Log($"[DialogHistory] '{p.name}' += {p.value} -> {current}");

                    OnParameterChanged?.Invoke(p.name, current);
                }
            }

            _currentRecord?.lines.Add(record);
            OnLineRecorded?.Invoke(record);
            return record;
        }

        // ---------- Parameters API ----------

        public float GetParameter(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0f;
            return _parameters.TryGetValue(name, out var v) ? v : 0f;
        }

        public void SetParameter(string name, float value)
        {
            if (string.IsNullOrEmpty(name)) return;
            _parameters[name] = value;
            OnParameterChanged?.Invoke(name, value);
        }

        public void AddParameter(string name, float delta)
        {
            if (string.IsNullOrEmpty(name)) return;
            float value = GetParameter(name) + delta;
            _parameters[name] = value;
            OnParameterChanged?.Invoke(name, value);
        }

        public void ResetParameters() => _parameters.Clear();

        public void ClearHistory()
        {
            _records.Clear();
            _parameters.Clear();
            _currentRecord = null;
            _sessionStartedUtc = DateTime.UtcNow.ToString("o");
        }

        // ---------- JSON ----------

        public HistorySnapshot CreateSnapshot()
        {
            var snapshot = new HistorySnapshot
            {
                sessionStartedUtc = _sessionStartedUtc
            };

            if (_records.Count > 0) snapshot.dialogs.AddRange(_records);

            foreach (var kv in _parameters)
                snapshot.totals.Add(new ParameterEntry
                {
                    name = kv.Key,
                    value = kv.Value,
                    totalAfter = kv.Value
                });

            return snapshot;
        }

        public string ExportToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(CreateSnapshot(), prettyPrint);
        }

        public void ImportFromJson(string json, bool merge = false)
        {
            if (string.IsNullOrEmpty(json)) return;

            var snapshot = JsonUtility.FromJson<HistorySnapshot>(json);
            if (snapshot == null) return;

            if (!merge)
            {
                _records.Clear();
                _parameters.Clear();
                _sessionStartedUtc = string.IsNullOrEmpty(snapshot.sessionStartedUtc)
                    ? DateTime.UtcNow.ToString("o")
                    : snapshot.sessionStartedUtc;
            }

            if (snapshot.dialogs != null && snapshot.dialogs.Count > 0)
                _records.AddRange(snapshot.dialogs);

            if (snapshot.totals != null)
            {
                foreach (var t in snapshot.totals)
                {
                    if (t == null || string.IsNullOrEmpty(t.name)) continue;
                    float prev = merge ? GetParameter(t.name) : 0f;
                    _parameters[t.name] = prev + t.value;
                }
            }
        }

        // ---------- Persistence ----------

        public void Save()
        {
            string json = ExportToJson(false);

            if (usePlayerPrefs)
            {
                PlayerPrefs.SetString(playerPrefsKey, json);
                PlayerPrefs.Save();
            }
            else
            {
                try
                {
                    string path = ResolvePath(filePath);
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllText(path, json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DialogHistory] Save failed: {e.Message}");
                }
            }
        }

        public void Load()
        {
            try
            {
                string json;
                if (usePlayerPrefs)
                {
                    if (!PlayerPrefs.HasKey(playerPrefsKey)) return;
                    json = PlayerPrefs.GetString(playerPrefsKey);
                }
                else
                {
                    string path = ResolvePath(filePath);
                    if (!File.Exists(path)) return;
                    json = File.ReadAllText(path);
                }

                ImportFromJson(json, false);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DialogHistory] Load failed: {e.Message}");
            }
        }

        public void ExportToFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, ExportToJson(true));
                Debug.Log($"[DialogHistory] Exported to: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DialogHistory] ExportToFile failed: {e.Message}");
            }
        }

        public void ImportFromFile(string path, bool merge = false)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                ImportFromJson(File.ReadAllText(path), merge);
                Debug.Log($"[DialogHistory] Imported from: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DialogHistory] ImportFromFile failed: {e.Message}");
            }
        }

        private static string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (Path.IsPathRooted(path)) return path;
            return Path.Combine(Application.persistentDataPath, path);
        }
    }

#if UNITY_EDITOR
    // =========================================================================
    //  Custom inspector
    // =========================================================================

    [CustomEditor(typeof(DialogHistory))]
    public class DialogHistoryEditor : Editor
    {
        private bool _showParameters = true;
        private bool _showDialogs = true;
        private bool _showRawJson;
        private string _rawJson = "";

        private readonly HashSet<string> _expandedDialogs = new HashSet<string>();
        private readonly HashSet<string> _expandedLines = new HashSet<string>();

        private class ParamStat
        {
            public string name;
            public float total;
            public int touches;
            public string lastUtc;
        }

        public override void OnInspectorGUI()
        {
            var history = (DialogHistory)target;

            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            DrawToolbar(history);

            EditorGUILayout.Space(6);
            DrawSummary(history);

            EditorGUILayout.Space(6);
            DrawParameters(history);

            EditorGUILayout.Space(6);
            DrawDialogs(history);

            EditorGUILayout.Space(6);
            DrawJsonTools(history);

            if (Application.isPlaying) Repaint();
        }

        // ---------------------------------------------------------------------

        private void DrawToolbar(DialogHistory history)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    history.Save();
                    Debug.Log("[DialogHistory] Save requested.");
                }

                if (GUILayout.Button("Load", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    history.Load();
                    Debug.Log("[DialogHistory] Load requested.");
                }

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    Repaint();

                GUILayout.FlexibleSpace();

                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    if (EditorUtility.DisplayDialog(
                            "Clear history",
                            "Удалить всю историю и сбросить параметры?",
                            "Yes", "No"))
                    {
                        Undo.RecordObject(history, "Clear Dialog History");
                        history.ClearHistory();
                        EditorUtility.SetDirty(history);
                    }
                }
                GUI.backgroundColor = prevColor;
            }
        }

        private void DrawSummary(DialogHistory history)
        {
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string started = string.IsNullOrEmpty(history.SessionStartedUtc)
                    ? "—"
                    : history.SessionStartedUtc;

                EditorGUILayout.LabelField("Session started (UTC)", started);
                EditorGUILayout.LabelField("Dialogs recorded", history.Records.Count.ToString());
                EditorGUILayout.LabelField("Parameters tracked", history.Parameters.Count.ToString());

                if (Application.isPlaying)
                    EditorGUILayout.LabelField("Live", "● updating", EditorStyles.miniLabel);
            }
        }

        private void DrawParameters(DialogHistory history)
        {
            _showParameters = EditorGUILayout.Foldout(_showParameters,
                $"Parameters / Points ({history.Parameters.Count})", true, EditorStyles.foldoutHeader);

            if (!_showParameters) return;

            if (history.Parameters.Count == 0)
            {
                EditorGUILayout.HelpBox("Пока нет накопленных очков.", MessageType.Info);
                return;
            }

            var stats = CollectParameterStats(history);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Parameter", EditorStyles.boldLabel, GUILayout.Width(160));
                    EditorGUILayout.LabelField("Total", EditorStyles.boldLabel, GUILayout.Width(70));
                    EditorGUILayout.LabelField("×", EditorStyles.boldLabel, GUILayout.Width(40));
                    EditorGUILayout.LabelField("Last change (UTC)", EditorStyles.boldLabel);
                }

                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

                foreach (var kv in history.Parameters)
                {
                    stats.TryGetValue(kv.Key, out var st);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(kv.Key, GUILayout.Width(160));

                        var prev = GUI.color;
                        GUI.color = kv.Value > 0
                            ? new Color(0.7f, 1f, 0.7f)
                            : (kv.Value < 0 ? new Color(1f, 0.7f, 0.7f) : Color.white);
                        EditorGUILayout.LabelField(kv.Value.ToString("0.###"), GUILayout.Width(70));
                        GUI.color = prev;

                        EditorGUILayout.LabelField(st != null ? st.touches.ToString() : "0", GUILayout.Width(40));
                        EditorGUILayout.LabelField(st != null ? st.lastUtc ?? "—" : "—");
                    }
                }
            }
        }

        private static Dictionary<string, ParamStat> CollectParameterStats(DialogHistory history)
        {
            var stats = new Dictionary<string, ParamStat>();

            foreach (var dlg in history.Records)
            {
                if (dlg?.lines == null) continue;

                foreach (var line in dlg.lines)
                {
                    if (line?.parameters == null) continue;

                    foreach (var p in line.parameters)
                    {
                        if (p == null || string.IsNullOrEmpty(p.name)) continue;

                        if (!stats.TryGetValue(p.name, out var st))
                        {
                            st = new ParamStat { name = p.name };
                            stats[p.name] = st;
                        }

                        st.touches++;
                        st.total = p.totalAfter;
                        st.lastUtc = line.timestampUtc;
                    }
                }
            }

            return stats;
        }

        private void DrawDialogs(DialogHistory history)
        {
            _showDialogs = EditorGUILayout.Foldout(_showDialogs,
                $"History ({history.Records.Count} dialogs)", true, EditorStyles.foldoutHeader);

            if (!_showDialogs) return;

            if (history.Records.Count == 0)
            {
                EditorGUILayout.HelpBox("История пуста. Запустите симуляцию и проиграйте диалог.",
                    MessageType.Info);
                return;
            }

            for (int i = history.Records.Count - 1; i >= 0; i--)
            {
                var dlg = history.Records[i];
                if (dlg == null) continue;
                DrawDialogRecord(dlg, i);
            }
        }

        private void DrawDialogRecord(DialogHistory.DialogRecord dlg, int index)
        {
            string key = $"{index}:{dlg.dialogId}:{dlg.startedAtUtc}";
            bool expanded = _expandedDialogs.Contains(key);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(expanded ? "▼" : "▶",
                            EditorStyles.label, GUILayout.Width(18)))
                    {
                        if (expanded) _expandedDialogs.Remove(key);
                        else _expandedDialogs.Add(key);
                        expanded = !expanded;
                    }

                    EditorGUILayout.LabelField(
                        $"#{index}  {dlg.dialogId}",
                        EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    int lineCount = dlg.lines != null ? dlg.lines.Count : 0;
                    EditorGUILayout.LabelField($"{lineCount} lines", GUILayout.Width(70));
                }

                EditorGUILayout.LabelField(
                    $"speaker: {dlg.speakerId}   •   start: {dlg.startedAtUtc}   •   end: {dlg.endedAtUtc}",
                    EditorStyles.miniLabel);

                if (!expanded) return;

                if (dlg.lines == null || dlg.lines.Count == 0)
                {
                    EditorGUILayout.LabelField("(no lines)", EditorStyles.miniLabel);
                    return;
                }

                EditorGUI.indentLevel++;

                for (int j = 0; j < dlg.lines.Count; j++)
                    DrawLineRecord(dlg.lines[j], key, j);

                EditorGUI.indentLevel--;
            }
        }

        private void DrawLineRecord(DialogHistory.LineRecord line, string dialogKey, int lineIndex)
        {
            if (line == null) return;

            string key = $"{dialogKey}|{lineIndex}";
            bool expanded = _expandedLines.Contains(key);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(expanded ? "▼" : "▶",
                            EditorStyles.label, GUILayout.Width(18)))
                    {
                        if (expanded) _expandedLines.Remove(key);
                        else _expandedLines.Add(key);
                        expanded = !expanded;
                    }

                    var label = string.IsNullOrEmpty(line.roleId)
                        ? line.lineId
                        : $"{line.roleId}: {line.lineId}";

                    if (line.isChoice)
                        label = "➜ " + (string.IsNullOrEmpty(line.selectedChoiceText)
                            ? label
                            : line.selectedChoiceText);

                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(line.timestampUtc, EditorStyles.miniLabel,
                        GUILayout.Width(220));
                }

                string preview = line.text ?? "";
                if (preview.Length > 140) preview = preview.Substring(0, 140) + "…";
                EditorGUILayout.LabelField(preview, EditorStyles.wordWrappedMiniLabel);

                if (!expanded) return;

                EditorGUI.indentLevel++;

                if (line.isChoice)
                {
                    EditorGUILayout.LabelField("Selected choice", line.selectedChoiceText ?? "—");
                    EditorGUILayout.LabelField("Target line", line.targetLineId ?? "—");
                }

                if (line.parameters != null && line.parameters.Count > 0)
                {
                    EditorGUILayout.LabelField("Parameter deltas", EditorStyles.boldLabel);
                    foreach (var p in line.parameters)
                    {
                        if (p == null) continue;
                        EditorGUILayout.LabelField(
                            $"{p.name}   {p.value:+0.###;-0.###;0}   → total {p.totalAfter:0.###}");
                    }
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawJsonTools(DialogHistory history)
        {
            _showRawJson = EditorGUILayout.Foldout(_showRawJson, "Raw JSON", true);
            if (!_showRawJson) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export...")) DialogHistoryMenu_Export(history);
                if (GUILayout.Button("Import...")) DialogHistoryMenu_Import(history);
                if (GUILayout.Button("Copy"))
                {
                    EditorGUIUtility.systemCopyBuffer = history.ExportToJson(true);
                    Debug.Log("[DialogHistory] JSON copied to clipboard.");
                }
                if (GUILayout.Button("Refresh JSON"))
                    _rawJson = history.ExportToJson(true);
            }

            if (string.IsNullOrEmpty(_rawJson))
                EditorGUILayout.HelpBox("Нажми «Refresh JSON», чтобы увидеть текущий снапшот.",
                    MessageType.None);

            _rawJson = EditorGUILayout.TextArea(_rawJson, GUILayout.MinHeight(120));
        }

        private static void DialogHistoryMenu_Export(DialogHistory history)
        {
            string path = EditorUtility.SaveFilePanel(
                "Export Dialog History", "", "dialog_history.json", "json");
            if (!string.IsNullOrEmpty(path)) history.ExportToFile(path);
        }

        private static void DialogHistoryMenu_Import(DialogHistory history)
        {
            string path = EditorUtility.OpenFilePanel("Import Dialog History", "", "json");
            if (!string.IsNullOrEmpty(path)) history.ImportFromFile(path, false);
        }
    }

    // =========================================================================
    //  Menu items
    // =========================================================================

    public static class DialogHistoryMenu
    {
        private const string MenuRoot = "Assets/Game/DialogHistory/";

        [MenuItem(MenuRoot + "Export selected History to JSON...", true)]
        [MenuItem(MenuRoot + "Import selected History from JSON...", true)]
        [MenuItem(MenuRoot + "Copy History JSON", true)]
        [MenuItem(MenuRoot + "Clear selected History", true)]
        private static bool ValidateSelection()
        {
            return Selection.activeObject is DialogHistory;
        }

        [MenuItem(MenuRoot + "Export selected History to JSON...")]
        private static void ExportSelected()
        {
            var history = Selection.activeObject as DialogHistory;
            if (history == null) return;

            string path = EditorUtility.SaveFilePanel(
                "Export Dialog History", "", "dialog_history.json", "json");
            if (string.IsNullOrEmpty(path)) return;

            history.ExportToFile(path);
        }

        [MenuItem(MenuRoot + "Import selected History from JSON...")]
        private static void ImportSelected()
        {
            var history = Selection.activeObject as DialogHistory;
            if (history == null) return;

            string path = EditorUtility.OpenFilePanel("Import Dialog History", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            history.ImportFromFile(path, false);
        }

        [MenuItem(MenuRoot + "Copy History JSON")]
        private static void CopySelected()
        {
            var history = Selection.activeObject as DialogHistory;
            if (history == null) return;

            EditorGUIUtility.systemCopyBuffer = history.ExportToJson(true);
            Debug.Log("[DialogHistory] JSON copied to clipboard.");
        }

        [MenuItem(MenuRoot + "Clear selected History")]
        private static void ClearSelected()
        {
            var history = Selection.activeObject as DialogHistory;
            if (history == null) return;

            if (EditorUtility.DisplayDialog("Clear history",
                "Удалить всю историю и сбросить параметры?", "Yes", "No"))
            {
                Undo.RecordObject(history, "Clear Dialog History");
                history.ClearHistory();
                EditorUtility.SetDirty(history);
            }
        }
    }
#endif
}