/* ============================================================================
 *  GamePlatform.jslib
 *  Путь: Assets/Plugins/WebGL/GamePlatform.jslib
 *
 *  Транспортный слой Unity WebGL ↔ Frontend Game Platform.
 *
 *  Правила:
 *   • Каждая функция принимает С-строку (указатель) и вызывает window.GamePlatform.<method>
 *     с уже распарсенным JavaScript-значением.
 *   • Парсинг JSON — на стороне JS. Unity шлёт просто строку, jslib её декодирует.
 *   • Все вызовы обёрнуты в try/catch, чтобы ошибка в JS не валила Unity.
 *   • Никакой бизнес-логики: только UTF8ToString, JSON.parse, вызов и лог.
 *
 *  Соответствие C# ↔ JS:
 *      GP_UnityReady()          → window.GamePlatform.unityReady()
 *      GP_Finish(json)          → window.GamePlatform.finish(parsedJson)
 *      GP_UpdateProgress(json)  → window.GamePlatform.updateProgress(parsedJson)
 *      GP_ExitToMenu()          → window.GamePlatform.toScenarios()
 *      GP_OpenLeaderboard(uuid) → window.GamePlatform.openLeaderboardByUuid(uuid)
 *      GP_Log(msg)              → console.log("[Unity]", msg)
 * ============================================================================ */

mergeInto(LibraryManager.library, {

  /* ------------------------------------------------------------------------
   *  Сообщить фронтенду, что мост готов принимать SendMessage.
   *  Вызывается один раз из GameBridge.Start().
   *  Без этого сигнала frontend держит исходящие сообщения в очереди.
   * -------------------------------------------------------------------- */
  GP_UnityReady: function () {
    try {
      if (window.GamePlatform && typeof window.GamePlatform.unityReady === "function") {
        window.GamePlatform.unityReady();
      } else {
        console.warn("[GP] GamePlatform.unityReady не найден");
      }
    } catch (e) {
      console.error("[GP] GP_UnityReady error:", e);
    }
  },

  /* ------------------------------------------------------------------------
   *  Финал сценария. Unity передаёт JSON-строку.
   *  Frontend сам отправит POST /api/v1/scenarios/finish.
   *
   *  Ожидаемая структура JSON:
   *    {
   *      "passenger_loyality": 12,
   *      "security_rating":    35,
   *      "duration_playtime":  145,         // опционально
   *      "result_json":        { ... }      // опционально, любая структура
   *    }
   * -------------------------------------------------------------------- */
  GP_Finish: function (jsonPtr) {
    try {
      var json = UTF8ToString(jsonPtr);
      if (!window.GamePlatform || typeof window.GamePlatform.finish !== "function") {
        console.error("[GP] GamePlatform.finish не найден");
        return;
      }

      var payload;
      try {
        payload = json ? JSON.parse(json) : {};
      } catch (e) {
        console.error("[GP] finish: некорректный JSON:", json, e);
        return;
      }

      // finish — асинхронный; промис не блокирует Unity.
      Promise.resolve(window.GamePlatform.finish(payload))
        .catch(function (err) {
          console.error("[GP] finish promise rejected:", err);
        });
    } catch (e) {
      console.error("[GP] GP_Finish error:", e);
    }
  },

  /* ------------------------------------------------------------------------
   *  Промежуточное обновление параметров — например, после каждого выбора.
   *  Frontend может обновить HUD, счётчик или прогресс-бар.
   *
   *  Ожидаемая структура:
   *    { "passenger_loyality": 5, "security_rating": 12 }
   * -------------------------------------------------------------------- */
  GP_UpdateProgress: function (jsonPtr) {
    try {
      var json = UTF8ToString(jsonPtr);
      if (!window.GamePlatform || typeof window.GamePlatform.updateProgress !== "function") {
        return;
      }
      var payload;
      try {
        payload = json ? JSON.parse(json) : null;
      } catch (e) {
        console.warn("[GP] updateProgress: плохой JSON:", json);
        return;
      }
      window.GamePlatform.updateProgress(payload);
    } catch (e) {
      console.error("[GP] GP_UpdateProgress error:", e);
    }
  },

  /* ------------------------------------------------------------------------
   *  Игрок вышел из сценария в меню (без завершения).
   *  Frontend показывает список сценариев и шлёт в Unity OnScenarioStop.
   * -------------------------------------------------------------------- */
  GP_ExitToMenu: function () {
    try {
      if (window.GamePlatform && typeof window.GamePlatform.toScenarios === "function") {
        window.GamePlatform.toScenarios();
      }
    } catch (e) {
      console.error("[GP] GP_ExitToMenu error:", e);
    }
  },

  /* ------------------------------------------------------------------------
   *  Попросить frontend открыть рейтинг конкретного сценария.
   *  Unity передаёт UUID строкой.
   * -------------------------------------------------------------------- */
  GP_OpenLeaderboard: function (uuidPtr) {
    try {
      var uuid = UTF8ToString(uuidPtr);
      if (!uuid) return;
      if (window.GamePlatform && typeof window.GamePlatform.openLeaderboardByUuid === "function") {
        window.GamePlatform.openLeaderboardByUuid(uuid);
      }
    } catch (e) {
      console.error("[GP] GP_OpenLeaderboard error:", e);
    }
  },

  /* ------------------------------------------------------------------------
   *  Удобный лог из Unity в консоль браузера.
   * -------------------------------------------------------------------- */
  GP_Log: function (msgPtr) {
    try {
      var msg = UTF8ToString(msgPtr);
      console.log("[Unity]", msg);
    } catch (e) {
      /* тихо игнорируем — логи не должны валить игру */
    }
  }
});