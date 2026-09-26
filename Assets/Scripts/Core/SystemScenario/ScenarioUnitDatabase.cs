using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game
{
    /// <summary>
    /// ScriptableObject-каталог сценарийных юнитов.
    /// Хранит имена юнитов и списки сообщений, которые они принимают и отправляют.
    /// Используется как справочник/справочная БД для редактора и для валидации сообщений.
    /// </summary>
    [CreateAssetMenu(fileName = "ScenarioUnitDatabase", menuName = "Game/Scenario Unit Database")]
    public class ScenarioUnitDatabase : ScriptableObject
    {
        [SerializeField] private List<ScenarioUnitDefinition> _units = new List<ScenarioUnitDefinition>();

        public IReadOnlyList<ScenarioUnitDefinition> Units => _units;

        public ScenarioUnitDefinition Find(string unitName)
        {
            if (string.IsNullOrEmpty(unitName)) return null;

            for (int i = 0; i < _units.Count; i++)
            {
                var u = _units[i];
                if (u != null && u.UnitName == unitName) return u;
            }

            return null;
        }

        public bool Contains(string unitName) => Find(unitName) != null;

        /// <summary>Возвращает true, если указанный юнит умеет принимать такое сообщение.</summary>
        public bool CanReceive(string unitName, string message)
        {
            var u = Find(unitName);
            return u != null && u.ReceiveMessages.Contains(message);
        }

        /// <summary>Возвращает true, если указанный юнит может отправить такое сообщение.</summary>
        public bool CanSend(string unitName, string message)
        {
            var u = Find(unitName);
            return u != null && u.SendMessages.Contains(message);
        }

#if UNITY_EDITOR
        public ScenarioUnitDefinition AddUnit(string unitName)
        {
            if (string.IsNullOrEmpty(unitName)) return null;
            if (Contains(unitName)) return Find(unitName);

            var def = new ScenarioUnitDefinition { UnitName = unitName };
            _units.Add(def);
            EditorUtility.SetDirty(this);
            return def;
        }

        public bool RemoveUnit(string unitName)
        {
            var def = Find(unitName);
            if (def == null) return false;

            _units.Remove(def);
            EditorUtility.SetDirty(this);
            return true;
        }

        /// <summary>Собирает в БД всех ScenarioUnit-ы, найденные на сцене.</summary>
        public void CollectFromScene(bool overwriteExisting = false)
        {
            var units = FindObjectsOfType<ScenarioUnit>(true);
            foreach (var unit in units)
            {
                if (unit == null) continue;

                var def = Find(unit.ScenarioUnitName);
                if (def == null)
                {
                    _units.Add(new ScenarioUnitDefinition
                    {
                        UnitName = unit.ScenarioUnitName
                    });
                }
                else if (overwriteExisting)
                {
                    def.Clear();
                }
            }

            EditorUtility.SetDirty(this);
        }
#endif
    }


    [Serializable]
    public class ScenarioUnitDefinition
    {
        [SerializeField] private string _unitName;

        [Tooltip("Сообщения, которые юнит принимает из сценария.")]
        [SerializeField] private List<string> _receiveMessages = new List<string>();

        [Tooltip("Сообщения, которые юнит отправляет в сценарий.")]
        [SerializeField] private List<string> _sendMessages = new List<string>();

        public string UnitName
        {
            get => _unitName;
            set => _unitName = value;
        }

        public List<string> ReceiveMessages => _receiveMessages;
        public List<string> SendMessages => _sendMessages;

        public void Clear()
        {
            _receiveMessages.Clear();
            _sendMessages.Clear();
        }
    }


#if UNITY_EDITOR
    // =====================================================================
    //  Custom inspector для ScenarioUnitDatabase
    // =====================================================================

    [CustomEditor(typeof(ScenarioUnitDatabase))]
    public class ScenarioUnitDatabaseEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var db = (ScenarioUnitDatabase)target;

            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Collect from scene (add missing)"))
                {
                    Undo.RecordObject(db, "Collect ScenarioUnits");
                    db.CollectFromScene(false);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Collect from scene (overwrite)"))
                {
                    if (EditorUtility.DisplayDialog(
                            "Overwrite entries?",
                            "Все существующие receive/send сообщения будут очищены.",
                            "Yes", "Cancel"))
                    {
                        Undo.RecordObject(db, "Overwrite ScenarioUnits");
                        db.CollectFromScene(true);
                    }
                }
            }
        }
    }
#endif
}