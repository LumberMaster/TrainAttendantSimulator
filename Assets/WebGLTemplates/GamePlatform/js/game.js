import { api } from "./api.js";
import { CONFIG } from "./config.js";
import { State } from "./state.js";
import { $, showScreen, toast, fmtTime, escapeHtml } from "./ui.js";

export async function startScenario(scenario) {
  State.activeScenario = scenario;
  State.activeResultId = null;
  $("#hud-scenario").textContent = scenario.name;
  $("#hud-timer").textContent = "00:00";
  $("#game-hud").style.display = "none";

  showScreen("screen-game");

  try {
    await ensureUnity();
  } catch (ex) {
    toast("Не удалось загрузить игру: " + ex.message, "err");
    return;
  }

  try {
    const res = await api(CONFIG.SCENARIOS.start, {
      method: "POST",
      body: { scenario_uuid: scenario.scenario_uuid },
    });
    State.activeResultId = res.id;
    State.gameStartedAt = performance.now();
    startTimer();
    $("#game-hud").style.display = "block";

    // Опциональный мост в Unity: GameObject "GameBridge" с методом OnScenarioStarted(string).
    bridgeToUnity("OnScenarioStarted", JSON.stringify({
      result_id: res.id,
      scenario_uuid: scenario.scenario_uuid,
      scenario_name: scenario.name,
    }));
  } catch (ex) {
    toast("Не удалось начать сценарий: " + ex.message, "err");
  }
}

export function leaveGame() {
  resetGame();
  showScreen("screen-scenarios");
}

export function resetGame() {
  stopTimer();
  $("#game-hud").style.display = "none";
  State.activeScenario = null;
  State.activeResultId = null;
}

function startTimer() {
  stopTimer();
  State.timerHandle = setInterval(() => {
    const s = (performance.now() - State.gameStartedAt) / 1000;
    $("#hud-timer").textContent = fmtTime(s);
  }, 500);
}

export function stopTimer() {
  if (State.timerHandle) {
    clearInterval(State.timerHandle);
    State.timerHandle = null;
  }
}

function ensureUnity() {
  if (State.unity) return Promise.resolve(State.unity);
  if (State.unityLoading) return State.unityLoading;

  const canvas  = $("#unity-canvas");
  const loading = $("#unity-loading");
  const bar     = $("#unity-progress");
  const barText = $("#unity-progress-text");
  loading.classList.remove("hidden");

  // window.UNITY_BUILD задан инлайново в index.html (Unity подставит пути).
  const build = window.UNITY_BUILD || {};

  State.unityLoading = createUnityInstance(canvas, build, (p) => {
    const pct = Math.round(p * 100);
    bar.style.width = pct + "%";
    barText.textContent = pct + "%";
  }).then((instance) => {
    State.unity = instance;
    loading.classList.add("hidden");
    return instance;
  }).catch((err) => {
    State.unityLoading = null;
    loading.innerHTML =
      `<div style="color:var(--danger)">Ошибка загрузки: ${escapeHtml(String(err))}</div>`;
    throw err;
  });

  return State.unityLoading;
}

/**
 * Опциональный мост в Unity: если объекта GameBridge в сцене нет,
 * вызов молча игнорируется.
 */
function bridgeToUnity(method, payload) {
  if (!State.unity) return;
  try {
    State.unity.SendMessage("GameBridge", method, payload || "");
  } catch (_) { /* GameBridge не обязателен */ }
}

/**
 * Публичное API для Unity:
 *   window.GamePlatform.finish({ passenger_loyality, security_rating })
 *   window.GamePlatform.toScenarios()
 *   window.GamePlatform.openLeaderboardByUuid(uuid)
 */
export function initGamePlatform() {
  window.GamePlatform = {
    state: () => ({
      user: State.user,
      scenario: State.activeScenario,
      result_id: State.activeResultId,
    }),

    async finish({ passenger_loyality, security_rating, duration_playtime, result_json = null }) {
      if (!State.activeResultId) throw new Error("Сценарий не запущен");
      const duration = duration_playtime ??
        Math.round((performance.now() - State.gameStartedAt) / 1000);

      const res = await api(CONFIG.SCENARIOS.finish, {
        method: "POST",
        body: {
          result_id: State.activeResultId,
          passenger_loyality: Math.max(0, Math.round(passenger_loyality || 0)),
          security_rating:    Math.max(0, Math.round(security_rating    || 0)),
          duration_playtime:  Math.max(0, Math.round(duration)),
          result_json: result_json ?? null,
        },
      });

      stopTimer();
      toast("Результат сохранён", "ok");
      return res;
    },

    toScenarios: leaveGame,

    openLeaderboardByUuid(uuid) {
      const s = State.scenarios.find(x => x.scenario_uuid === uuid);
      if (s) {
        // Динамический импорт — избегаем циклической зависимости.
        import("./leaderboard.js").then(m => m.openLeaderboard(s));
      }
    },
  };
}