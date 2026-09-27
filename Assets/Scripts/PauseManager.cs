using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PauseManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject pauseMenu;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button quitButton;

    [Header("Input")]
    [Tooltip("Action типа Button (например Escape)")]
    [SerializeField] private InputActionReference pauseAction;

    [Header("Cursor")]
    [Tooltip("Показывать и разблокировать курсор при паузе")]
    [SerializeField] private bool showCursorOnPause = true;

    private bool isPaused = false;

    private void OnEnable()
    {
        if (pauseAction != null && pauseAction.action != null)
        {
            pauseAction.action.Enable();
            pauseAction.action.performed += OnPausePerformed;
        }

        if (resumeButton != null)
            resumeButton.onClick.AddListener(Resume);

        if (quitButton != null)
            quitButton.onClick.AddListener(QuitGame);
    }

    private void OnDisable()
    {
        if (pauseAction != null && pauseAction.action != null)
        {
            pauseAction.action.performed -= OnPausePerformed;
            pauseAction.action.Disable();
        }

        if (resumeButton != null)
            resumeButton.onClick.RemoveListener(Resume);

        if (quitButton != null)
            quitButton.onClick.RemoveListener(QuitGame);
    }

    private void Start()
    {
        pauseMenu.SetActive(false);
        Time.timeScale = 1f;
        isPaused = false;
        // Курсор на старте НЕ трогаем — им управляет другой скрипт/настройки сцены
    }

    private void OnPausePerformed(InputAction.CallbackContext ctx)
    {
        TogglePause();
    }

    public void TogglePause()
    {
        if (isPaused) Resume();
        else Pause();
    }

    public void Pause()
    {
        isPaused = true;
        pauseMenu.SetActive(true);
        Time.timeScale = 0f;
        AudioListener.pause = true;

        if (showCursorOnPause)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (resumeButton != null)
            resumeButton.Select();
    }

    public void Resume()
    {
        isPaused = false;
        pauseMenu.SetActive(false);
        Time.timeScale = 1f;
        AudioListener.pause = false;

        // Курсор НЕ трогаем — остаётся в том состоянии, в котором был
        // (видимый, разблокированный и т.п.)
    }

    public void QuitGame()
    {
        Debug.Log("Выход из игры...");
        Time.timeScale = 1f;
        AudioListener.pause = false;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}