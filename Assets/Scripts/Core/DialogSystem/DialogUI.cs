using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public class DialogUI : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private CanvasGroup panel;

        [Header("Line Widgets")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text lineText;
        [SerializeField] private Image portrait;

        [Header("Choices")]
        [Tooltip("Префаб кнопки-варианта. Должен содержать DialogChoiceButton.")]
        [SerializeField] private DialogChoiceButton choiceButtonPrefab;
        [SerializeField] private RectTransform choicesContainer;

        [Header("Timer")]
        [Tooltip("Текст обратного отсчёта.")]
        [SerializeField] private TMP_Text timerText;

        [Header("Options")]
        [SerializeField] private bool hideWhenNoPortrait = true;

        private readonly List<DialogChoiceButton> _spawnedChoices = new List<DialogChoiceButton>();

        private void Awake() => Hide();

        // ---------- Panel ----------

        public void Show()
        {
            if (panel != null)
            {
                panel.alpha = 1f;
                panel.blocksRaycasts = true;
                panel.interactable = true;
            }
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

            HideChoices();
            SetTimerActive(false, 0f);
        }

        // ---------- Line ----------

        /// <summary>Имя берём из БД — по roleId реплики.</summary>
        public void SetLine(DialogRole role, DialogLine line)
        {
            if (nameText != null)
            {
                Debug.Log(role.displayName);
                if (role != null)
                {
                    nameText.text = role.displayName;
                    nameText.color = role.nameColor;
                }
                else
                {
                    nameText.text = string.Empty;
                    nameText.color = Color.white;
                }
            }

            if (lineText != null)
                lineText.text = line != null ? line.text : string.Empty;

            if (portrait != null)
            {
                portrait.sprite = role != null ? role.portrait : null;
                portrait.enabled = portrait.sprite != null || !hideWhenNoPortrait;
            }
        }

        // ---------- Choices ----------

        public void ShowChoices(IReadOnlyList<DialogLineTransition> choices, Action<int> onSelect)
        {
            HideChoices();

            if (choiceButtonPrefab == null || choicesContainer == null || choices == null) return;

            for (int i = 0; i < choices.Count; i++)
            {
                var tr = choices[i];
                if (tr == null) continue;

                var btn = Instantiate(choiceButtonPrefab, choicesContainer);
                if (btn.Label != null)
                    btn.Label.text = string.IsNullOrEmpty(tr.choiceText) ? "..." : tr.choiceText;

                int idx = i;
                if (btn.Button != null)
                    btn.Button.onClick.AddListener(() => onSelect?.Invoke(idx));

                _spawnedChoices.Add(btn);
            }

            if (choicesContainer.gameObject.activeSelf == false)
                choicesContainer.gameObject.SetActive(true);
        }

        public void HideChoices()
        {
            for (int i = 0; i < _spawnedChoices.Count; i++)
                if (_spawnedChoices[i] != null)
                    Destroy(_spawnedChoices[i].gameObject);

            _spawnedChoices.Clear();
        }

        // ---------- Timer ----------

        public void SetTimerActive(bool active, float duration)
        {
            if (timerText == null) return;

            timerText.gameObject.SetActive(active);
            timerText.text = active ? Mathf.Max(0, Mathf.CeilToInt(duration)).ToString() : string.Empty;
        }

        public void SetTimer(float remaining)
        {
            if (timerText == null) return;
            if (!timerText.gameObject.activeSelf) return;

            timerText.text = Mathf.Max(0, Mathf.CeilToInt(remaining)).ToString();
        }
    }
}