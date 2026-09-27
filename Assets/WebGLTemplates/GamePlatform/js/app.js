/* ============================ CONFIG ============================ */
const CONFIG = {
  API_BASE: "http://91.229.11.11:8001",
  AUTH: {
    login:    "/api/v1/auth/login",
    register: "/api/v1/auth/register",
    me:       "/api/v1/auth/me",
  },
  SCENARIOS: {
    list:          "/api/v1/scenarios/",
    start:         "/api/v1/scenarios/start",
    finish:        "/api/v1/scenarios/finish",
    leaderboard:   "/api/v1/scenarios/leaderboard",
    results:       "/api/v1/scenarios/results",
    resultsByUser: "/api/v1/scenarios/results/{user_uuid}",
  },
  TOKEN_KEY: "gp_token",
  USER_KEY:  "gp_user",
};

/* ============================ STATE ============================= */
const State = {
  user: null,
  scenarios: [],
  activeScenario: null,
  activeResultId: null,
  gameStartedAt: 0,
  unity: null,
  unityLoading: null,
  timerHandle: null,
};

/* ============================= UI =============================== */
const $  = (s) => document.querySelector(s);
const $$ = (s) => Array.from(document.querySelectorAll(s));

function showScreen(id) {
  $$(".screen").forEach(s => s.classList.toggle("active", s.id === id));
}

let toastTimer = null;
function toast(msg, kind = "") {
  const el = $("#toast");
  el.textContent = msg;
  el.className = "show " + kind;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.className = kind; }, 3200);
}

function setBusy(btn, busy, text = "…") {
  if (!btn) return;
  if (busy) {
    btn.dataset._t = btn.textContent;
    btn.disabled = true;
    btn.innerHTML = `<span class="spinner"></span> ${text}`;
  } else {
    btn.disabled = false;
    btn.textContent = btn.dataset._t || btn.textContent;
  }
}

function fmtTime(sec) {
  sec = Math.max(0, Math.floor(sec));
  const h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
  const p = n => String(n).padStart(2, "0");
  return h > 0 ? `${p(h)}:${p(m)}:${p(s)}` : `${p(m)}:${p(s)}`;
}

function fmtDate(iso) {
  if (!iso) return "—";
  const d = new Date(iso);
  return isNaN(d) ? iso : d.toLocaleString("ru-RU", { dateStyle: "short", timeStyle: "short" });
}

const escapeHtml = (s) => String(s ?? "").replace(/[&<>"']/g, c =>
  ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

/* ============================ TOKEN ============================= */
function saveToken(t) { try { localStorage.setItem(CONFIG.TOKEN_KEY, t); } catch {} }
function getToken()   { try { return localStorage.getItem(CONFIG.TOKEN_KEY); } catch { return null; } }
function clearToken() { try { localStorage.removeItem(CONFIG.TOKEN_KEY); } catch {} }

function decodeJwt(token) {
  if (!token) return null;
  const parts = token.split(".");
  if (parts.length !== 3) return null;
  try {
    const b64 = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const padded = b64 + "===".slice((b64.length + 3) % 4);
    const json = decodeURIComponent(
      atob(padded).split("").map(c => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2)).join("")
    );
    return JSON.parse(json);
  } catch { return null; }
}

function isTokenExpired(token) {
  const p = decodeJwt(token);
  if (!p || !p.exp) return false; // подпись/срок всё равно проверит сервер
  return Date.now() >= p.exp * 1000 - 5000;
}

/* ============================= API ============================== */
async function api(path, { method = "GET", body = null, auth = true } = {}) {
  const opts = { method, headers: { "Accept": "application/json" } };
  if (body !== null) {
    opts.headers["Content-Type"] = "application/json";
    opts.body = JSON.stringify(body);
  }
  if (auth) {
    const t = getToken();
    if (t && !isTokenExpired(t)) opts.headers["Authorization"] = "Bearer " + t;
  }

  const res = await fetch((CONFIG.API_BASE || "") + path, opts);

  if (res.status === 401 && auth) {
    clearToken();
    const e = new Error("Сессия истекла"); e.status = 401; throw e;
  }

  const text = await res.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }

  if (!res.ok) {
    const e = new Error(formatError(data) || `HTTP ${res.status}`);
    e.status = res.status; e.payload = data; throw e;
  }
  return data;
}

function formatError(p) {
  if (!p) return "";
  if (typeof p === "string") return p;
  if (Array.isArray(p.detail))
    return p.detail.map(e => `${(e.loc || []).slice(1).join(".")}: ${e.msg}`).join("; ");
  if (p.detail) return typeof p.detail === "string" ? p.detail : JSON.stringify(p.detail);
  return JSON.stringify(p);
}

/* ============================= AUTH ============================= */
function renderUserChips() {
  const u = State.user;
  const txt = u
    ? `<b>${escapeHtml(u.first_name || "")} ${escapeHtml(u.second_name || "")}</b> · ${escapeHtml(u.email || "")}`
    : "";
  const a = $("#user-info"); if (a) a.innerHTML = txt;
  const b = $("#lb-user");   if (b) b.innerHTML = txt;
}

function saveUser(u) {
  try {
    u ? localStorage.setItem(CONFIG.USER_KEY, JSON.stringify(u))
      : localStorage.removeItem(CONFIG.USER_KEY);
  } catch {}
}

function loadUserFromStorage() {
  try { const r = localStorage.getItem(CONFIG.USER_KEY); return r ? JSON.parse(r) : null; }
  catch { return null; }
}

function resetAuthForms() {
  ["form-login", "form-register"].forEach(id => {
    const f = document.getElementById(id); if (f) f.reset();
  });
  const le = $("#login-err");    if (le) le.textContent = "";
  const re = $("#register-err"); if (re) re.textContent = "";
}

const extractToken = (r) => r?.access_token || r?.accessToken || r?.token || r?.jwt || null;

function extractUser(r, fallback) {
  if (r?.user && typeof r.user === "object") return r.user;
  if (r && typeof r === "object") {
    const { access_token, accessToken, token, jwt, token_type, expires_in, ...rest } = r;
    if (rest && (rest.user_uuid || rest.email || rest.id)) return rest;
  }
  return fallback || null;
}

async function login(email, password) {
  const resp = await api(CONFIG.AUTH.login, { method: "POST", body: { email, password }, auth: false });
  const token = extractToken(resp);
  if (!token) throw new Error("Сервер не вернул токен");
  saveToken(token);
  State.user = extractUser(resp, { email });
  saveUser(State.user);
  return State.user;
}

async function register(payload) {
  const resp = await api(CONFIG.AUTH.register, { method: "POST", body: payload, auth: false });
  const token = extractToken(resp);
  if (!token) throw new Error("Сервер не вернул токен");
  saveToken(token);
  State.user = extractUser(resp, {
    email: payload.email, first_name: payload.first_name, second_name: payload.second_name,
  });
  saveUser(State.user);
  return State.user;
}

function logout() { clearToken(); saveUser(null); State.user = null; }

/* ========================== SCENARIOS =========================== */
async function loadScenarios() {
  const grid  = $("#scenarios-grid");
  const empty = $("#scenarios-empty");
  grid.innerHTML = `<div class="empty" style="grid-column:1/-1">Загрузка…</div>`;
  empty.style.display = "none";

  const list = await api(CONFIG.SCENARIOS.list);
  State.scenarios = list || [];
  renderScenarios();
}

function renderScenarios() {
  const grid  = $("#scenarios-grid");
  const empty = $("#scenarios-empty");
  grid.innerHTML = "";

  if (!State.scenarios.length) {
    empty.style.display = "block";
    renderScenarioFilter();
    return;
  }

  for (const s of State.scenarios) {
    const card = document.createElement("div");
    card.className = "card";
    card.innerHTML = `
      <h3>${escapeHtml(s.name)}</h3>
      <p>${s.description ? escapeHtml(s.description) : "<i style='opacity:.5'>Без описания</i>"}</p>
      <div class="actions">
        <button class="play" type="button">▶ Играть</button>
        <button class="board ghost" type="button">🏆 Рейтинг</button>
      </div>`;
    card.querySelector(".play").addEventListener("click",  () => startScenario(s));
    card.querySelector(".board").addEventListener("click", () => openLeaderboard(s));
    grid.appendChild(card);
  }

  renderScenarioFilter();
}

function renderScenarioFilter() {
  const sel = $("#lb-scenario-filter");
  if (!sel) return;
  const current = sel.value;
  sel.innerHTML =
    `<option value="">Все сценарии</option>` +
    State.scenarios.map(s =>
      `<option value="${escapeHtml(s.scenario_uuid)}">${escapeHtml(s.name)}</option>`
    ).join("");
  if (current && State.scenarios.some(s => s.scenario_uuid === current)) {
    sel.value = current;
  }
}

/* ========================= LEADERBOARD ========================== */
function setLeaderboardHint() {
  const t = $("#lb-title"); if (t) t.textContent = "Рейтинг";
  const c = $("#lb-content");
  if (c) c.innerHTML = `<div class="empty">Выберите сценарий, чтобы увидеть рейтинг</div>`;
}

async function openLeaderboard(scenario) {
  if (!scenario) return;
  $("#lb-title").textContent = "Рейтинг — " + scenario.name;

  const sel = $("#lb-scenario-filter");
  if (sel) sel.value = scenario.scenario_uuid;

  const box = $("#lb-content");
  box.innerHTML = `<div class="empty">Загрузка…</div>`;
  try {
    const entries = await api(CONFIG.SCENARIOS.leaderboard, {
      method: "POST",
      body: { scenario_uuid: scenario.scenario_uuid },
    });
    renderLeaderboard(entries || []);
  } catch (ex) {
    box.innerHTML = `<div class="empty">Ошибка: ${escapeHtml(ex.message)}</div>`;
  }
}

function renderLeaderboard(entries) {
  const box = $("#lb-content");
  if (!entries.length) {
    box.innerHTML = `<div class="empty">Пока никто не прошёл этот сценарий.<br>
      <span style="font-size:12px;opacity:.6">Стань первым!</span></div>`;
    return;
  }

  const rows = entries.map((e, i) => {
    const place = i + 1;
    const cls = place <= 3 ? ` class="p${place}"` : "";
    const loy = e.passenger_loyality ?? "—";
    const sec = e.security_rating    ?? "—";
    const end = e.time_end ? fmtDate(e.time_end) : "—";
    return `<tr${cls}>
      <td class="place">${place}</td>
      <td>${escapeHtml(e.first_name)} ${escapeHtml(e.second_name)}</td>
      <td class="num">${fmtTime(e.duration_playtime)}</td>
      <td class="num">${loy}</td>
      <td class="num">${sec}</td>
      <td style="color:var(--muted);font-size:12px">${end}</td>
    </tr>`;
  }).join("");

  box.innerHTML = `
    <div class="lb-head"><h2>Топ игроков по времени прохождения</h2></div>
    <table class="lb-table">
      <thead><tr>
        <th>#</th><th>Игрок</th><th>Время</th>
        <th>Лояльность</th><th>Безопасность</th><th>Завершено</th>
      </tr></thead>
      <tbody>${rows}</tbody>
    </table>`;
}

/* =========================== RESULTS ============================ */
async function openMyResults() {
  if (!State.user?.user_uuid) { toast("Нет данных пользователя", "err"); return; }
  await openResultsModal("Мои результаты", CONFIG.SCENARIOS.results);
}

async function openUserResults(userUuid, title) {
  if (!userUuid) { toast("Не указан пользователь", "err"); return; }
  const path = CONFIG.SCENARIOS.resultsByUser.replace(
    "{user_uuid}", encodeURIComponent(userUuid)
  );
  await openResultsModal(title || "Результаты игрока", path);
}

async function openResultsModal(title, path) {
  const modal = $("#results-modal");
  const body  = $("#results-body");
  $("#results-title").textContent = title;
  body.innerHTML = `<div class="empty">Загрузка…</div>`;
  modal.classList.add("show");
  modal.setAttribute("aria-hidden", "false");
  try {
    const results = await api(path);
    renderResults(results || []);
  } catch (ex) {
    body.innerHTML = `<div class="empty">Ошибка: ${escapeHtml(ex.message)}</div>`;
  }
}

function closeResultsModal() {
  const modal = $("#results-modal");
  if (!modal) return;
  modal.classList.remove("show");
  modal.setAttribute("aria-hidden", "true");
}

function renderResults(results) {
  const body = $("#results-body");
  if (!results.length) {
    body.innerHTML = `<div class="empty">Пока нет завершённых прохождений</div>`;
    return;
  }

  const sorted = [...results].sort((a, b) => {
    const ta = a.time_end ? Date.parse(a.time_end) : 0;
    const tb = b.time_end ? Date.parse(b.time_end) : 0;
    return tb - ta;
  });

  const rows = sorted.map(r => {
    const name = r.scenario_name || r.scenario_uuid || "—";
    const dur  = r.duration_playtime != null ? fmtTime(r.duration_playtime) : "—";
    const loy  = r.passenger_loyality ?? "—";
    const sec  = r.security_rating    ?? "—";
    const end  = r.time_end ? fmtDate(r.time_end) : "—";
    return `<tr>
      <td>${escapeHtml(name)}</td>
      <td class="num">${dur}</td>
      <td class="num">${loy}</td>
      <td class="num">${sec}</td>
      <td style="color:var(--muted);font-size:12px">${end}</td>
    </tr>`;
  }).join("");

  body.innerHTML = `
    <table class="lb-table">
      <thead><tr>
        <th>Сценарий</th><th>Время</th><th>Лояльность</th>
        <th>Безопасность</th><th>Завершено</th>
      </tr></thead>
      <tbody>${rows}</tbody>
    </table>
    <div class="results-summary">Всего прохождений: <b>${results.length}</b></div>`;
}

/* ============================ GAME ============================== */
function showGameStart(show) {
  const el = $("#game-start");
  if (el) el.style.display = show ? "flex" : "none";
}

async function ensureUnity() {
  if (State.unity) return State.unity;
  if (State.unityLoading) return State.unityLoading;

  const canvas  = $("#unity-canvas");
  const loading = $("#unity-loading");
  const bar     = $("#unity-progress");
  const barText = $("#unity-progress-text");

  loading.style.display = "flex";

  const build = window.UNITY_BUILD || {};

  State.unityLoading = createUnityInstance(canvas, build, (p) => {
    const pct = Math.round(p * 100);
    bar.style.width = pct + "%";
    barText.textContent = pct + "%";
  }).then((inst) => {
    State.unity = inst;
    loading.style.display = "none";
    return inst;
  }).catch((err) => {
    State.unityLoading = null;
    loading.innerHTML =
      `<div style="color:var(--danger)">Ошибка загрузки: ${escapeHtml(String(err))}</div>`;
    throw err;
  });

  return State.unityLoading;
}

async function startScenario(scenario) {
  State.activeScenario = scenario;
  State.activeResultId = null;
  $("#hud-scenario").textContent = scenario.name;
  $("#hud-timer").textContent = "00:00";

  // Показываем игровой экран (внутри всё ещё оверлей выбора), затем загружаем движок.
  showScreen("screen-game");

  try {
    await ensureUnity();
  } catch (ex) {
    toast("Не удалось загрузить игру: " + ex.message, "err");
    return;
  }

  showGameStart(false);

  try {
    const res = await api(CONFIG.SCENARIOS.start, {
      method: "POST",
      body: { scenario_uuid: scenario.scenario_uuid },
    });
    State.activeResultId = res.id;
    State.gameStartedAt = performance.now();
    startTimer();

    bridgeToUnity("OnScenarioStarted", JSON.stringify({
      result_id: res.id,
      scenario_uuid: scenario.scenario_uuid,
      scenario_name: scenario.name,
    }));
  } catch (ex) {
    toast("Не удалось начать сценарий: " + ex.message, "err");
    showGameStart(true);
  }
}

function leaveGame() {
  resetGame();
  showGameStart(true);
}

function resetGame() {
  stopTimer();
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

function stopTimer() {
  if (State.timerHandle) { clearInterval(State.timerHandle); State.timerHandle = null; }
}

function bridgeToUnity(method, payload) {
  if (!State.unity) return;
  try { State.unity.SendMessage("GameBridge", method, payload || ""); } catch {}
}

function initGamePlatform() {
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
      if (s) openLeaderboard(s);
    },

    /* --- results --- */
    async getMyResults() {
      return api(CONFIG.SCENARIOS.results);
    },

    async getUserResults(userUuid) {
      const path = CONFIG.SCENARIOS.resultsByUser.replace(
        "{user_uuid}", encodeURIComponent(userUuid)
      );
      return api(path);
    },

    openMyResults,
    openUserResults,
  };
}

/* ============================ MAIN ============================== */
function switchAuthTab(which) {
  const isLogin = which === "login";
  $("#tab-login")?.classList.toggle("active", isLogin);
  $("#tab-register")?.classList.toggle("active", !isLogin);
  const fl = $("#form-login");    if (fl) fl.style.display = isLogin ? "" : "none";
  const fr = $("#form-register"); if (fr) fr.style.display = isLogin ? "none" : "";
}

async function enterApp() {
  renderUserChips();
  showScreen("screen-game");
  showGameStart(true);       // показываем выбор сценария
  setLeaderboardHint();      // правая колонка — подсказка до выбора
  try {
    await loadScenarios();
  } catch (ex) {
    if (ex.status === 401) {
      State.user = null;
      try { localStorage.removeItem(CONFIG.USER_KEY); } catch {}
      renderUserChips();
      showScreen("screen-auth");
      switchAuthTab("login");
      toast("Сессия истекла, войдите заново", "err");
    } else {
      const grid = $("#scenarios-grid");
      if (grid) grid.innerHTML =
        `<div class="empty" style="grid-column:1/-1">Ошибка загрузки: ${escapeHtml(ex.message)}</div>`;
    }
  }
}

/* ------ listeners ------ */
$("#tab-login")?.addEventListener("click",    () => switchAuthTab("login"));
$("#tab-register")?.addEventListener("click", () => switchAuthTab("register"));

$("#form-login")?.addEventListener("submit", async (e) => {
  e.preventDefault();
  const err = $("#login-err"); err.textContent = "";
  const btn = $("#login-submit");
  setBusy(btn, true, "Вход…");
  try {
    await login($("#login-email").value.trim(), $("#login-pass").value);
    await enterApp();
  } catch (ex) {
    err.textContent = "Ошибка: " + ex.message;
  } finally {
    setBusy(btn, false);
  }
});

$("#form-register")?.addEventListener("submit", async (e) => {
  e.preventDefault();
  const err = $("#register-err"); err.textContent = "";
  const btn = $("#register-submit");
  setBusy(btn, true, "Создание…");
  try {
    const third = $("#reg-third").value.trim();
    await register({
      email:       $("#reg-email").value.trim(),
      password:    $("#reg-pass").value,
      first_name:  $("#reg-first").value.trim(),
      second_name: $("#reg-second").value.trim(),
      third_name:  third || null,
    });
    await enterApp();
  } catch (ex) {
    err.textContent = "Ошибка: " + ex.message;
  } finally {
    setBusy(btn, false);
  }
});

$("#btn-logout")?.addEventListener("click", () => {
  logout();
  resetGame();
  showGameStart(true);
  setLeaderboardHint();
  renderUserChips();
  resetAuthForms();
  closeResultsModal();
  showScreen("screen-auth");
  switchAuthTab("login");
  toast("Вы вышли из аккаунта");
});

$("#game-back")?.addEventListener("click", leaveGame);

$("#lb-scenario-filter")?.addEventListener("change", (e) => {
  const uuid = e.target.value;
  if (!uuid) { setLeaderboardHint(); return; }
  const s = State.scenarios.find(x => x.scenario_uuid === uuid);
  if (s) openLeaderboard(s);
});

$("#btn-my-results")?.addEventListener("click", openMyResults);
$("#results-close")?.addEventListener("click", closeResultsModal);
$("#results-modal")?.addEventListener("click", (e) => {
  if (e.target === $("#results-modal")) closeResultsModal();
});
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") closeResultsModal();
});

initGamePlatform();

/* ------ bootstrap ------ */
(async function init() {
  State.user = loadUserFromStorage();
  const token = getToken();
  if (token && !isTokenExpired(token)) {
    await enterApp();
  } else {
    clearToken();
    showScreen("screen-auth");
    switchAuthTab("login");
  }
  console.log("[GamePlatform] UI ready");
})();