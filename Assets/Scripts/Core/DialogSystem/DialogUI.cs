using System;
using System.Collections;
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
        [Tooltip("Слайдер обратного отсчёта (заполняется по мере уменьшения времени).")]
        [SerializeField] private Slider timerSlider;

        [Header("Next Button")]
        [Tooltip("Кнопка 'Далее'. Появляется после завершения печати/аудио реплики " +
                 "и служит сигналом для перехода к следующей реплике. " +
                 "Если нажата во время печати — печать мгновенно завершается.")]
        [SerializeField] private Button nextButton;

        [Header("Options")]
        [SerializeField] private bool hideWhenNoPortrait = true;

        [Header("Typewriter")]
        [Tooltip("Сколько символов в секунду печатается. 0 или меньше — без анимации.")]
        [SerializeField] private float charactersPerSecond = 40f;
        [Tooltip("Пауза после символов . ! ? … , ; : (в секундах). 0 — отключено.")]
        [SerializeField] private float punctuationPause = 0.15f;
        [Tooltip("Также печатать имя посимвольно.")]
        [SerializeField] private bool animateName = false;

        [Header("Portrait (RenderTexture)")]
        [Tooltip("RawImage, в который выводится портрет текущего говорящего.")]
        [SerializeField] private RawImage portraitRawImage;

        private readonly List<DialogChoiceButton> _spawnedChoices = new List<DialogChoiceButton>();

        private Coroutine _typeLineRoutine;
        private Coroutine _typeNameRoutine;

        private string _lineFullText = string.Empty;
        private string _nameFullText = string.Empty;

        /// <summary>Полная длительность текущего таймера (для нормализации слайдера).</summary>
        private float _timerDuration = 1f;

        /// <summary>true, пока идёт печать текста реплики.</summary>
        public bool IsTyping { get; private set; }

        /// <summary>Вызывается, когда печать реплики завершена (или была прервана/пропущена).</summary>
        public event Action LineTypingCompleted;

        /// <summary>Вызывается при нажатии на кнопку «Далее» (когда печать уже завершена).</summary>
        public event Action NextRequested;

        private void Awake()
        {
            if (nextButton != null)
            {
                // Никакой навигации/Submit — кнопку можно нажать только настоящим кликом.
                nextButton.navigation = new Navigation { mode = Navigation.Mode.None };

                // На всякий случай перевешиваем persistent-слушатели, оставшиеся в инспекторе.
                nextButton.onClick = new Button.ButtonClickedEvent();
                nextButton.onClick.AddListener(OnNextButtonClicked);
            }

            Hide();
        }

        // ---------- Next Button ----------

        private void OnNextButtonClicked()
        {
            // Если текст ещё печатается — мгновенно показываем его целиком
            // и ждём следующего нажатия для перехода к следующей реплике.
            if (IsTyping)
            {
                CompleteTyping();
                return;
            }

            NextRequested?.Invoke();
        }

        /// <summary>Показать/скрыть кнопку «Далее».</summary>
        public void SetNextButtonVisible(bool visible)
        {
            if (nextButton == null) return;

            if (nextButton.gameObject.activeSelf != visible)
                nextButton.gameObject.SetActive(visible);
        }

        // ---------- Panel ----------

        public void Show()
        {
            if (panel == null) return;

            panel.alpha = 1f;
            panel.blocksRaycasts = true;
            panel.interactable = true;
        }

        public void Hide()
        {
            StopAllTyping();

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
            SetNextButtonVisible(false);
        }

        // ---------- Line ----------

        /// <summary>Имя берём из БД — по roleId реплики.</summary>
        public void SetLine(DialogRole role, DialogLine line)
        {
            // Имя
            if (nameText != null)
            {
                if (role != null)
                {
                    nameText.color = role.nameColor;

                    if (animateName) StartNameTyping(role.displayName);
                    else nameText.text = role.displayName;
                }
                else
                {
                    StopNameTyping();
                    nameText.text = string.Empty;
                    nameText.color = Color.white;
                }
            }

            // Спрайтовый портрет (если задан)
            if (portrait != null)
            {
                portrait.sprite = role != null ? role.portrait : null;
                portrait.enabled = portrait.sprite != null || !hideWhenNoPortrait;
            }

            // Текст реплики
            StartLineTyping(line != null ? line.text : string.Empty);
        }

        /// <summary>
        /// Мгновенно завершает печать.
        /// Возвращает true, если что-то было пропущено.
        /// </summary>
        public bool CompleteTyping()
        {
            if (!IsTyping) return false;

            var full = _lineFullText;
            StopAllTyping();

            if (lineText != null) lineText.text = full;
            if (nameText != null && animateName) nameText.text = _nameFullText;

            LineTypingCompleted?.Invoke();
            return true;
        }

        // ---------- Typewriter internals ----------

        private void StartLineTyping(string text)
        {
            StopLineTyping();
            _lineFullText = text ?? string.Empty;

            if (lineText == null) return;

            if (charactersPerSecond <= 0f || string.IsNullOrEmpty(_lineFullText))
            {
                lineText.text = _lineFullText;
                IsTyping = false;
                LineTypingCompleted?.Invoke();
                return;
            }

            // maxVisibleCharacters работает быстро и не создаёт мусор в строках.
            lineText.text = _lineFullText;
            lineText.maxVisibleCharacters = 0;
            lineText.ForceMeshUpdate();

            _typeLineRoutine = StartCoroutine(TypeLineRoutine());
        }

        private IEnumerator TypeLineRoutine()
        {
            IsTyping = true;

            int total = lineText.textInfo.characterCount;
            float delay = 1f / charactersPerSecond;

            for (int i = 1; i <= total; i++)
            {
                lineText.maxVisibleCharacters = i;

                // Пауза после знаков препинания
                if (punctuationPause > 0f)
                {
                    char c = lineText.textInfo.characterInfo[i - 1].character;
                    if (c == '.' || c == '!' || c == '?' || c == '…' ||
                        c == ',' || c == ';' || c == ':')
                    {
                        yield return new WaitForSeconds(delay + punctuationPause);
                        continue;
                    }
                }

                yield return new WaitForSeconds(delay);
            }

            lineText.maxVisibleCharacters = total;
            _typeLineRoutine = null;
            IsTyping = false;
            LineTypingCompleted?.Invoke();
        }

        private void StopLineTyping()
        {
            if (_typeLineRoutine != null)
            {
                StopCoroutine(_typeLineRoutine);
                _typeLineRoutine = null;
            }
            IsTyping = false;
        }

        private void StopAllTyping()
        {
            StopLineTyping();
            StopNameTyping();
        }

        private void StartNameTyping(string text)
        {
            StopNameTyping();
            _nameFullText = text ?? string.Empty;

            if (nameText == null) return;

            if (charactersPerSecond <= 0f || string.IsNullOrEmpty(_nameFullText))
            {
                nameText.text = _nameFullText;
                return;
            }

            nameText.text = _nameFullText;
            nameText.maxVisibleCharacters = 0;
            nameText.ForceMeshUpdate();

            _typeNameRoutine = StartCoroutine(TypeNameRoutine());
        }

        private IEnumerator TypeNameRoutine()
        {
            int total = nameText.textInfo.characterCount;
            float delay = 1f / charactersPerSecond;

            for (int i = 1; i <= total; i++)
            {
                nameText.maxVisibleCharacters = i;
                yield return new WaitForSeconds(delay);
            }

            nameText.maxVisibleCharacters = total;
            _typeNameRoutine = null;
        }

        private void StopNameTyping()
        {
            if (_typeNameRoutine != null)
            {
                StopCoroutine(_typeNameRoutine);
                _typeNameRoutine = null;
            }

            if (nameText != null)
                nameText.maxVisibleCharacters = int.MaxValue;
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

            if (!choicesContainer.gameObject.activeSelf)
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
            _timerDuration = Mathf.Max(0.0001f, duration);

            if (timerText != null)
            {
                timerText.gameObject.SetActive(active);
                timerText.text = active
                    ? Mathf.Max(0, Mathf.CeilToInt(duration)).ToString()
                    : string.Empty;
            }

            if (timerSlider != null)
            {
                timerSlider.gameObject.SetActive(active);

                if (active)
                {
                    timerSlider.minValue = 0f;
                    timerSlider.maxValue = 1f;
                    timerSlider.value = 1f;
                }
            }
        }

        public void SetTimer(float remaining)
        {
            if (timerText != null && timerText.gameObject.activeSelf)
                timerText.text = Mathf.Max(0, Mathf.CeilToInt(remaining)).ToString();

            if (timerSlider != null && timerSlider.gameObject.activeSelf)
                timerSlider.value = Mathf.Clamp01(remaining / _timerDuration);
        }

        // ---------- Portrait (RenderTexture) ----------

        /// <summary>
        /// Устанавливает текстуру портрета. Передайте null, чтобы скрыть портрет.
        /// </summary>
        public void SetRenderTexture(RenderTexture renderTexture)
        {
            if (portraitRawImage == null) return;

            portraitRawImage.texture = renderTexture;
            portraitRawImage.enabled = renderTexture != null;
        }
    }
}