using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public class DialogUI : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private CanvasGroup panel;

        [Header("Widgets")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text lineText;
        [SerializeField] private Image portrait;

        [Header("Options")]
        [SerializeField] private bool hideWhenNoPortrait = true;

        private void Awake()
        {
            Hide();
        }

        public void Show(DialogSpeaker speaker, Dialog dialog)
        {
            if (panel != null)
            {
                panel.alpha = 1f;
                panel.blocksRaycasts = true;
                panel.interactable = true;
            }

            if (nameText != null && speaker != null && !string.IsNullOrEmpty(speaker.DisplayName))
                nameText.text = speaker.DisplayName;
        }

        public void Hide()
        {
            if (panel != null)
            {
                panel.alpha = 0f;
                panel.blocksRaycasts = false;
                panel.interactable = false;
            }

            if (nameText != null) nameText.text = string.Empty;
            if (lineText != null) lineText.text = string.Empty;

            if (portrait != null)
            {
                portrait.sprite = null;
                portrait.enabled = false;
            }
        }

        public void SetLine(DialogSpeaker speaker, DialogRole role, DialogLine line)
        {
            // Имя
            if (nameText != null)
            {
                string name = null;

                if (speaker != null && role != null && speaker.UnitName == role.roleId && !string.IsNullOrEmpty(speaker.DisplayName))
                    name = speaker.DisplayName;
                else if (role != null)
                    name = !string.IsNullOrEmpty(role.displayName) ? role.displayName : role.roleId;

                nameText.text = name ?? string.Empty;

                if (role != null) nameText.color = role.nameColor;
            }

            // Текст
            if (lineText != null)
                lineText.text = line != null ? line.text : string.Empty;

            // Портрет
            if (portrait != null)
            {
                Sprite sprite = null;

                if (speaker != null && role != null && speaker.UnitName == role.roleId && speaker.OverridePortrait != null)
                    sprite = speaker.OverridePortrait;
                else if (role != null)
                    sprite = role.portrait;

                portrait.sprite = sprite;
                portrait.enabled = sprite != null || !hideWhenNoPortrait;
            }
        }
    }
}