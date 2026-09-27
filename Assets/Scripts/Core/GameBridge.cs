using System;
using System.Runtime.InteropServices;
using UnityEngine;

// =====================================================================
//  GameBridge — мост между Unity WebGL и фронтендом Game Platform.
//
//  • Фронтенд → Unity: SendMessage("GameBridge", method, json)
//      OnScenarioStarted(json)  — запустить сценарий
//      OnScenarioStop(json)     — игрок вышел (сбросить состояние)
//      OnResultSaved(json)      — сервер принял результат
//      OnResultSaveFailed(json) — сервер отклонил
//
//  • Unity → Frontend: [DllImport("__Internal")] → GamePlatform.jslib
// =====================================================================

[Serializable]
public class ScenarioStartUser
{
    public string user_uuid;
    public string email;
    public string first_name;
    public string second_name;
    public string third_name;
    public int level;
    public int xp;
}

[Serializable]
public class ScenarioStartPayload
{
    public string result_id;
    public string scenario_uuid;
    public string scenario_name;
    public string scenario_description;
    public ScenarioStartUser user;
}

public class GameBridge : MonoBehaviour
{
    public static GameBridge Instance { get; private set; }

    // ---- Нативные функции, реализованные в GamePlatform.jslib ----
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void GP_UnityReady();
    [DllImport("__Internal")] private static extern void GP_Finish(string json);
    [DllImport("__Internal")] private static extern void GP_UpdateProgress(string json);
    [DllImport("__Internal")] private static extern void GP_ExitToMenu();
    [DllImport("__Internal")] private static extern void GP_OpenLeaderboard(string uuid);
    [DllImport("__Internal")] private static extern void GP_Log(string msg);
#endif

    // ---- События для игровой логики ----
    /// <summary>Сценарий запущен фронтендом. Подписывайтесь и грузите нужную сцену.</summary>
    public event Action<ScenarioStartPayload> ScenarioStarted;

    /// <summary>Фронтенд сообщил, что игрок вышел в меню выбора сценария.</summary>
    public event Action ScenarioStopped;

    /// <summary>Сервер принял результат.</summary>
    public event Action<string> ResultSaved;

    /// <summary>Сервер отклонил результат. payload — JSON с полем error.</summary>
    public event Action<string> ResultSaveFailed;

    // ---- Состояние текущего сценария ----
    public ScenarioStartPayload Current { get; private set; }
    public string ResultId => Current?.result_id;
    public string ScenarioUuid => Current?.scenario_uuid;
    public bool IsRunning { get; private set; }

    private float _startedAt;

    // ------------------------------------------------------------------
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Сообщаем фронтенду, что мост готов принимать SendMessage.
        Log("GameBridge.Start() → GP_UnityReady()");
#if UNITY_WEBGL && !UNITY_EDITOR
        GP_UnityReady();
#else
        Debug.Log("[GameBridge] Editor: GP_UnityReady() пропущен");
#endif
    }

    // ==================================================================
    //  ВЫЗОВЫ ИЗ ФРОНТЕНДА (SendMessage)
    // ==================================================================

    /// <summary>
    /// Фронтенд вызывает: unityInstance.SendMessage("GameBridge",
    /// "OnScenarioStarted", jsonString).
    /// </summary>
    public void OnScenarioStarted(string json)
    {
        ScenarioStartPayload data = null;
        try { data = JsonUtility.FromJson<ScenarioStartPayload>(json); }
        catch (Exception e) { Debug.LogError("[GameBridge] OnScenarioStarted parse: " + e.Message); }

        Current = data;
        IsRunning = true;
        _startedAt = Time.realtimeSinceStartup;

        Debug.Log($"[GameBridge] ▶ Scenario started: {data?.scenario_name} ({data?.scenario_uuid}), result_id={data?.result_id}");

        ScenarioStarted?.Invoke(data);

        // Пример: если у каждого сценария своя сцена —
        // SceneManager.LoadScene(data.scenario_uuid);
    }

    /// <summary>Фронтенд вызывает при выходе игрока в меню.</summary>
    public void OnScenarioStop(string json)
    {
        Debug.Log("[GameBridge] ■ Scenario stop: " + json);
        IsRunning = false;
        Current = null;
        ScenarioStopped?.Invoke();
    }

    /// <summary>Фронтенд подтверждает, что результат сохранён на сервере.</summary>
    public void OnResultSaved(string json)
    {
        Debug.Log("[GameBridge] ✔ Result saved: " + json);
        ResultSaved?.Invoke(json);
    }

    /// <summary>Фронтенд сообщает, что сохранить не удалось.</summary>
    public void OnResultSaveFailed(string json)
    {
        Debug.LogError("[GameBridge] ✖ Result save failed: " + json);
        ResultSaveFailed?.Invoke(json);
    }

    // ==================================================================
    //  ПУБЛИЧНЫЕ МЕТОДЫ ДЛЯ ИГРОВОГО КОДА (Unity → Frontend)
    // ==================================================================

    /// <summary>
    /// Завершить сценарий и передать результат на фронтенд.
    /// Фронтенд сам отправит POST /api/v1/scenarios/finish.
    /// </summary>
    /// <param name="passengerLoyality">Лояльность пассажира (целое).</param>
    /// <param name="securityRating">Рейтинг безопасности (целое).</param>
    /// <param name="resultJson">
    ///   Уже сериализованный JSON с произвольной структурой (например,
    ///   полная стенограмма диалогов). Может быть null — тогда на сервер
    ///   уйдёт result_json = null.
    /// </param>
    /// <param name="durationPlaytime">
    ///   Если &lt; 0 — фронтенд сам посчитает по своему таймеру.
    /// </param>
    public void FinishScenario(
        int passengerLoyality,
        int securityRating,
        string resultJson = null,
        int durationPlaytime = -1)
    {
        if (!IsRunning)
        {
            Debug.LogWarning("[GameBridge] FinishScenario: сценарий не запущен");
            return;
        }
        IsRunning = false;

        int duration = durationPlaytime >= 0
            ? durationPlaytime
            : Mathf.RoundToInt(Time.realtimeSinceStartup - _startedAt);

        // Собираем JSON вручную, чтобы аккуратно вставить resultJson как вложенный объект/массив.
        string payload =
            "{" +
                "\"passenger_loyality\":" + passengerLoyality + "," +
                "\"security_rating\":" + securityRating + "," +
                "\"duration_playtime\":" + duration + "," +
                "\"result_json\":" + (string.IsNullOrEmpty(resultJson) ? "null" : resultJson) +
            "}";

        Debug.Log("[GameBridge] Finish → " + payload);

#if UNITY_WEBGL && !UNITY_EDITOR
        GP_Finish(payload);
#else
        Debug.Log("[GameBridge] Editor: GP_Finish(" + payload + ")");
#endif
    }

    /// <summary>Опционально: промежуточное обновление — фронт покажет в HUD.</summary>
    public void UpdateProgress(int passengerLoyality, int securityRating)
    {
        string payload =
            "{" +
                "\"passenger_loyality\":" + passengerLoyality + "," +
                "\"security_rating\":" + securityRating +
            "}";
#if UNITY_WEBGL && !UNITY_EDITOR
        GP_UpdateProgress(payload);
#endif
    }

    /// <summary>Выйти в меню выбора сценария (фронт покажет список).</summary>
    public void ExitToMenu()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GP_ExitToMenu();
#endif
    }

    /// <summary>Открыть рейтинг сценария на фронте (правая колонка).</summary>
    public void OpenLeaderboard(string scenarioUuid)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GP_OpenLeaderboard(scenarioUuid);
#endif
    }

    /// <summary>Удобный лог в консоль браузера.</summary>
    public void Log(string msg)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GP_Log(msg);
#else
        Debug.Log(msg);
#endif
    }
}