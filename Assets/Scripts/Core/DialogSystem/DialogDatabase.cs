using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    [CreateAssetMenu(fileName = "DialogDatabase", menuName = "Game/Dialog Database")]
    public class DialogDatabase : ScriptableObject
    {
        [SerializeField] private List<DialogRole> roles = new List<DialogRole>();
        [SerializeField] private List<Dialog> dialogs = new List<Dialog>();

        public IReadOnlyList<DialogRole> Roles => roles;
        public IReadOnlyList<Dialog> Dialogs => dialogs;

        public DialogRole GetRole(string roleId)
        {
            if (string.IsNullOrEmpty(roleId)) return null;
            for (int i = 0; i < roles.Count; i++)
                if (roles[i] != null && roles[i].roleId == roleId) return roles[i];
            return null;
        }

        public Dialog GetDialog(string dialogId)
        {
            if (string.IsNullOrEmpty(dialogId)) return null;
            for (int i = 0; i < dialogs.Count; i++)
                if (dialogs[i] != null && dialogs[i].id == dialogId) return dialogs[i];
            return null;
        }
    }

    [Serializable]
    public class DialogRole
    {
        [Tooltip("Уникальный id роли (используется в DialogLine.roleId).")]
        public string roleId;

        [Tooltip("Отображаемое имя (например, 'Стражник').")]
        public string displayName;

        [Tooltip("Цвет имени в UI (опционально).")]
        public Color nameColor = Color.white;

        [Tooltip("Портрет/аватар (опционально).")]
        public Sprite portrait;
    }

    [Serializable]
    public class Dialog
    {
        [Tooltip("Уникальный строковый id диалога.")]
        public string id;

        [TextArea(1, 3)] public string description;

        public List<DialogLine> lines = new List<DialogLine>();
    }

    [Serializable]
    public class DialogLine
    {
        [Tooltip("id роли, которая произносит реплику.")]
        public string roleId;

        [TextArea(2, 6)] public string text;

        public AudioClip audio;

        [Tooltip("Если аудио нет — сколько секунд показывать строку.")]
        public float fallbackDuration = 2f;
    }
}