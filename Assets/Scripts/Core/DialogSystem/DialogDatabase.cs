using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game
{
    [CreateAssetMenu(fileName = "DialogDatabase", menuName = "Game/Dialog Database")]
    public class DialogDatabase : ScriptableObject
    {
        [Header("Roles")]
        [SerializeField] private List<DialogRole> roles = new List<DialogRole>();

        [Header("Dialogs (each is a separate ScriptableObject)")]
        [SerializeField] private List<DialogAsset> dialogs = new List<DialogAsset>();

        public IReadOnlyList<DialogRole> Roles => roles;
        public IReadOnlyList<DialogAsset> Dialogs => dialogs;

        public DialogRole GetRole(string roleId)
        {
            if (string.IsNullOrEmpty(roleId)) return null;
            for (int i = 0; i < roles.Count; i++)
                if (roles[i] != null && roles[i].roleId == roleId) return roles[i];
            return null;
        }

        public DialogAsset GetDialog(string dialogId)
        {
            if (string.IsNullOrEmpty(dialogId)) return null;
            for (int i = 0; i < dialogs.Count; i++)
                if (dialogs[i] != null && dialogs[i].id == dialogId) return dialogs[i];
            return null;
        }


#if UNITY_EDITOR
        // =====================================================================
        //  EDITOR-ONLY: JSON export / import (roles + list of dialog asset paths)
        // =====================================================================

        public string ExportToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(ToJsonData(), prettyPrint);
        }

        public void ImportFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("JSON is empty.");

            var data = JsonUtility.FromJson<DialogDatabaseJsonData>(json);
            if (data == null)
                throw new Exception("Failed to parse dialog database JSON.");

            FromJsonData(data);

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        private DialogDatabaseJsonData ToJsonData()
        {
            var data = new DialogDatabaseJsonData
            {
                roles = new List<DialogRoleJsonData>(),
                dialogPaths = new List<string>()
            };

            if (roles != null)
            {
                foreach (var r in roles)
                {
                    if (r == null) continue;

                    data.roles.Add(new DialogRoleJsonData
                    {
                        roleId = r.roleId,
                        displayName = r.displayName,
                        nameColor = r.nameColor,
                        portraitPath = r.portrait != null
                            ? AssetDatabase.GetAssetPath(r.portrait)
                            : string.Empty
                    });
                }
            }

            if (dialogs != null)
            {
                foreach (var d in dialogs)
                {
                    if (d == null) continue;
                    var path = AssetDatabase.GetAssetPath(d);
                    if (!string.IsNullOrEmpty(path))
                        data.dialogPaths.Add(path);
                }
            }

            return data;
        }

        private void FromJsonData(DialogDatabaseJsonData data)
        {
            roles = new List<DialogRole>();
            dialogs = new List<DialogAsset>();

            if (data.roles != null)
            {
                foreach (var r in data.roles)
                {
                    if (r == null) continue;

                    roles.Add(new DialogRole
                    {
                        roleId = r.roleId,
                        displayName = r.displayName,
                        nameColor = r.nameColor,
                        portrait = string.IsNullOrEmpty(r.portraitPath)
                            ? null
                            : AssetDatabase.LoadAssetAtPath<Sprite>(r.portraitPath)
                    });
                }
            }

            if (data.dialogPaths != null)
            {
                foreach (var path in data.dialogPaths)
                {
                    if (string.IsNullOrEmpty(path)) continue;

                    var asset = AssetDatabase.LoadAssetAtPath<DialogAsset>(path);
                    if (asset != null) dialogs.Add(asset);
                    else Debug.LogWarning($"[DialogDatabase] Dialog asset not found at: {path}");
                }
            }
        }
#endif
    }


    [Serializable]
    public class DialogRole
    {
        [Tooltip("Уникальный id роли (используется в DialogLine.roleId).")]
        public string roleId;

        [Tooltip("Отображаемое имя (то, что показывается в UI).")]
        public string displayName;

        [Tooltip("Цвет имени в UI.")]
        public Color nameColor = Color.white;

        [Tooltip("Портрет/аватар (опционально).")]
        public Sprite portrait;
    }


    [Serializable]
    public class DialogDatabaseJsonData
    {
        public List<DialogRoleJsonData> roles = new List<DialogRoleJsonData>();
        public List<string> dialogPaths = new List<string>();
    }

    [Serializable]
    public class DialogRoleJsonData
    {
        public string roleId;
        public string displayName;
        public Color nameColor = Color.white;
        public string portraitPath;
    }


#if UNITY_EDITOR
    [CustomEditor(typeof(DialogDatabase))]
    public class DialogDatabaseEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var db = (DialogDatabase)target;

            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("JSON (roles + dialog paths)", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export to file...")) ExportToFile(db);
                if (GUILayout.Button("Import from file...")) ImportFromFile(db);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy JSON"))
                {
                    EditorGUIUtility.systemCopyBuffer = db.ExportToJson(true);
                    Debug.Log("[DialogDatabase] JSON copied to clipboard.");
                }

                if (GUILayout.Button("Paste JSON"))
                {
                    string json = EditorGUIUtility.systemCopyBuffer;
                    if (string.IsNullOrEmpty(json))
                    {
                        Debug.LogWarning("[DialogDatabase] Clipboard is empty.");
                        return;
                    }
                    ApplyImport(db, json);
                }
            }
        }

        private static void ExportToFile(DialogDatabase db)
        {
            string defaultName = string.IsNullOrEmpty(db.name) ? "DialogDatabase" : db.name;

            string path = EditorUtility.SaveFilePanel(
                "Export Dialog Database to JSON", "", defaultName + ".json", "json");

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                System.IO.File.WriteAllText(path, db.ExportToJson(true));
                AssetDatabase.Refresh();
                Debug.Log($"[DialogDatabase] Exported to: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DialogDatabase] Export failed: {e.Message}");
            }
        }

        private static void ImportFromFile(DialogDatabase db)
        {
            string path = EditorUtility.OpenFilePanel("Import Dialog Database from JSON", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                string json = System.IO.File.ReadAllText(path);
                ApplyImport(db, json);
                Debug.Log($"[DialogDatabase] Imported from: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DialogDatabase] Import failed: {e.Message}");
            }
        }

        private static void ApplyImport(DialogDatabase db, string json)
        {
            Undo.RecordObject(db, "Import Dialog Database from JSON");
            db.ImportFromJson(json);
            GUIUtility.ExitGUI();
        }
    }
#endif
}