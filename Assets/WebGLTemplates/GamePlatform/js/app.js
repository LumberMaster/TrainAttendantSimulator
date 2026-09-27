import { GameBridge } from "./game-bridge.js";

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
    // start больше не используется — результат создаётся только в finish
    finish:        "/api/v1/scenarios/finish",
    leaderboard:   "/api/v1/scenarios/leaderboard",
    results:       "/api/v1/scenarios/results",
    resultsByUser: "/api/v1/scenarios/results/{user_uuid}",
  },
  TOKEN_KEY: "gp_token",
  USER_KEY:  "gp_user",
  XP_BASE:   100,
  UNITY_OBJECT: "GameBridge",
};

/* ============================ STATE ============================= */
const State = {
  user: null,
  scenarios: [],
  myResults: [],
  activeResult: null,
  activeScenario: null,
  gameStartedAt: 0,
  unity: null,
  unityLoading: null,
  timerHandle: null,
};

/* ============================= UI =============================== */
const $  = (s) => document.querySelector(s);
const $$ = (s) => Array.from(document.querySelectorAll(s));

function setAuthed(isAuthed) {
  document.body.classList.toggle("authed", !!isAuthed);
}

function switchToScreen(name) {
  $$(".screen").forEach(s => s.classList.toggle("active", s.id === "screen-" + name));
  $$("#app-nav button").forEach(b => b.classList.toggle("active", b.dataset.nav === name));
  document.body.classList.toggle("in-game", name === "game");
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
  if (!p || !p.exp) return false;
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

/* ========================== XP / LEVEL ========================== */
function xpToReachLevel(level) {
  if (level <= 1) return 0;
  return Math.round(CONFIG.XP_BASE * (level - 1) * level / 2);
}

function levelFromXp(xp) {
  xp = Math.max(0, Math.floor(Number(xp) || 0));
  let lvl = 1;
  while (xpToReachLevel(lvl + 1) <= xp) lvl++;
  return lvl;
}

const XP_KEYS = [
  "experience", "xp", "exp", "experience_points",
  "total_experience", "experience_total", "exp_total", "points", "score"
];

function extractXp(user) {
  if (!user) return 0;
  for (const k of XP_KEYS) {
    const v = user[k];
    if (typeof v === "number" && isFinite(v)) return v;
    if (typeof v === "string" && v.trim() !== "" && !isNaN(Number(v))) return Number(v);
  }
  return 0;
}

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
      </div>`;
    card.querySelector(".play").addEventListener("click", () => startScenario(s));
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

/* ========================= RESULTS LIST ========================= */
async function openResults() {
  const box = $("#results-list");
  box.innerHTML = `<div class="empty">Загрузка…</div>`;
  try {
    const results = await api(CONFIG.SCENARIOS.results);
    State.myResults = results || [];
    renderResultsList();
  } catch (ex) {
    box.innerHTML = `<div class="empty">Ошибка: ${escapeHtml(ex.message)}</div>`;
  }
}

function renderResultsList() {
  const box = $("#results-list");
  const results = State.myResults || [];
  if (!results.length) {
    box.innerHTML = `<div class="empty">Пока нет прохождений. Начните играть!</div>`;
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
    const hasDetails = !!(r.result_json) || !!(r.scenario_uuid);
    return `<tr data-id="${escapeHtml(r.id)}" class="${hasDetails ? "clickable" : ""}">
      <td>${escapeHtml(name)}</td>
      <td class="num">${dur}</td>
      <td class="num">${loy}</td>
      <td class="num">${sec}</td>
      <td style="color:var(--muted);font-size:12px">${end}</td>
      <td class="cell-link">${hasDetails ? "Подробнее →" : "—"}</td>
    </tr>`;
  }).join("");

  box.innerHTML = `
    <table class="lb-table results-table">
      <thead><tr>
        <th>Сценарий</th><th>Время</th><th>Лояльность</th>
        <th>Безопасность</th><th>Завершено</th><th></th>
      </tr></thead>
      <tbody>${rows}</tbody>
    </table>`;

  box.querySelectorAll("tr.clickable").forEach(tr => {
    tr.addEventListener("click", () => {
      const r = State.myResults.find(x => x.id === tr.dataset.id);
      if (r) openResultDetail(r);
    });
  });
}

/* ======================= RESULT DETAIL PAGE ===================== */
function openResultDetail(result) {
  State.activeResult = result;
  const titleEl = $("#result-title");
  titleEl.textContent = result.scenario_name || "Прохождение";

  const box = $("#result-detail");
  let data = result.result_json;

  if (typeof data === "string") {
    try { data = JSON.parse(data); } catch { data = null; }
  }

  if (!data || typeof data !== "object") {
    box.innerHTML = `<div class="empty">
      Нет подробных данных по этому прохождению.
      <div style="margin-top:10px;font-size:12px;opacity:.6">
        Сценарий может не передавать <code>result_json</code> при завершении.
      </div>
    </div>`;
    switchToScreen("result");
    return;
  }

  renderResultDetail(box, data, result);
  switchToScreen("result");
}

const RD_PALETTE = ["#5b8cff", "#34d399", "#fbbf24", "#f472b6", "#a78bfa", "#22d3ee", "#fb923c"];

const RD_RE = {
  directive: /(немедленно|прекрати|обязан|запрещ|должен|должны|покиньте|не имеете права|придётся|вынужден|не положено|немедля)/gi,
  empathy:   /(понимаю|сочувствую|извините|простите|сожалею|к сожалению|мне жаль|не переживайте|успокойтесь|волнуетесь|понимать)/gi,
  collab:    /(давайте|предлагаю|могу предложить|есть вариант|помогу|подскажу|решим|вызв|начальник|старший|альтернатив|вместе)/gi,
  escalate:  /(разнесу|полици|жалоб|скандал|не имеете права|вызову|только попробуй|всё здесь)/i,
};

function rdFmtNum(v) {
  if (typeof v !== "number" || !isFinite(v)) return String(v ?? "");
  return Number.isInteger(v) ? String(v) : v.toFixed(1);
}
function rdFmtTime(iso) {
  if (!iso) return "";
  const d = new Date(iso);
  return isNaN(d) ? iso : d.toLocaleTimeString("ru-RU", { hour12: false });
}
function rdFmtDur(a, b) {
  const ms = new Date(b) - new Date(a);
  if (!isFinite(ms) || ms < 0) return "";
  if (ms < 1000) return Math.round(ms) + " мс";
  if (ms < 60000) return (ms / 1000).toFixed(1) + " с";
  const m = Math.floor(ms / 60000), s = Math.round((ms % 60000) / 1000);
  return m + " мин " + s + " с";
}
function rdCount(str, re) { const m = String(str || "").match(re); return m ? m.length : 0; }
function rdIsPlayer(l)   { return !!(l.isChoice || l.roleId === "Player"); }

function rdGetTotals(data) {
  if (Array.isArray(data.totals) && data.totals.length) return data.totals;
  const map = {};
  (data.dialogs || []).forEach(d => (d.lines || []).forEach(l =>
    (l.parameters || []).forEach(p => {
      map[p.name] = (map[p.name] || 0) + (p.value || 0);
    })));
  return Object.keys(map).map(name => ({ name, value: map[name], totalAfter: map[name] }));
}

function renderResultDetail(container, data, meta) {
  const dialogs  = Array.isArray(data.dialogs) ? data.dialogs : [];
  const allLines = dialogs.flatMap(d => Array.isArray(d.lines) ? d.lines : []);
  const choices  = allLines.filter(rdIsPlayer);
  const npcLines = allLines.filter(l => !rdIsPlayer(l));

  container.innerHTML = `
    <div class="rd-meta">
      <div><span>Сценарий:</span> <b>${escapeHtml(meta.scenario_name || "—")}</b></div>
      ${meta.time_end ? `<div><span>Завершено:</span> <b>${escapeHtml(fmtDate(meta.time_end))}</b></div>` : ""}
      ${meta.duration_playtime != null ? `<div><span>Время:</span> <b>${escapeHtml(fmtTime(meta.duration_playtime))}</b></div>` : ""}
      ${meta.passenger_loyality != null ? `<div><span>Лояльность:</span> <b>${meta.passenger_loyality}</b></div>` : ""}
      ${meta.security_rating != null ? `<div><span>Безопасность:</span> <b>${meta.security_rating}</b></div>` : ""}
    </div>

    <section class="rd-stats" data-rd="stats"></section>

    <div class="rd-grid-2">
      <section class="rd-panel">
        <h2>Динамика параметров</h2>
        <p class="rd-hint">Накопительные показатели после каждого выбора игрока</p>
        <div data-rd="chart"></div>
        <div class="rd-legend" data-rd="legend"></div>
      </section>

      <section class="rd-panel">
        <h2>Компетенции: пробелы и сильные стороны</h2>
        <p class="rd-hint">Автоматическая оценка по репликам и параметрам диалога</p>
        <div data-rd="insights"></div>
      </section>
    </div>

    <section class="rd-panel">
      <h2>Итоговые параметры</h2>
      <p class="rd-hint">Суммарный результат сессии</p>
      <div class="rd-totals" data-rd="totals"></div>
    </section>

    <section class="rd-panel">
      <h2>Диалоги</h2>
      <p class="rd-hint">Полная стенограмма с подсветкой выборов и изменений параметров</p>
      <div data-rd="dialogs"></div>
    </section>
  `;

  const q = (n) => container.querySelector(`[data-rd="${n}"]`);
  rdRenderStats(q("stats"), data, dialogs, choices, npcLines);
  rdRenderChart(q("chart"), q("legend"), choices);
  rdRenderInsights(q("insights"), data, choices);
  rdRenderTotals(q("totals"), rdGetTotals(data));
  rdRenderDialogs(q("dialogs"), dialogs);
}

function rdRenderStats(box, data, dialogs, choices, npcLines) {
  const cards = [
    { k: "Старт сессии", v: data.sessionStartedUtc ? rdFmtTime(data.sessionStartedUtc) : "—",
      s: data.sessionStartedUtc ? new Date(data.sessionStartedUtc).toLocaleDateString("ru-RU") : "" },
    { k: "Диалогов", v: dialogs.length, s: "сценариев в сессии" },
    { k: "Выборов игрока", v: choices.length, s: "точек принятия решений" },
    { k: "Реплик NPC", v: npcLines.length, s: "реакций на действия" },
  ];
  box.innerHTML = cards.map(c => `
    <div class="rd-stat">
      <div class="k">${escapeHtml(c.k)}</div>
      <div class="v">${escapeHtml(String(c.v))}</div>
      <div class="s">${escapeHtml(c.s)}</div>
    </div>`).join("");
}

function rdRenderChart(box, legend, choices) {
  const names = [];
  choices.forEach(c => (c.parameters || []).forEach(p => {
    if (!names.includes(p.name)) names.push(p.name);
  }));

  if (!names.length) {
    box.innerHTML = `<p class="rd-muted">Нет данных о параметрах для построения графика.</p>`;
    legend.innerHTML = "";
    return;
  }

  const running = {};
  const points = {};
  names.forEach(n => points[n] = []);

  choices.forEach((c, i) => {
    (c.parameters || []).forEach(p => { running[p.name] = p.totalAfter; });
    names.forEach(n => points[n].push({ i, v: running[n] ?? 0 }));
  });

  const W = 820, H = 320;
  const pad = { t: 22, r: 26, b: 48, l: 58 };

  const allVals = [0].concat(names.flatMap(n => points[n].map(p => p.v)));
  let min = Math.min.apply(null, allVals);
  let max = Math.max.apply(null, allVals);
  if (min === max) { min -= 1; max += 1; }
  const padV = (max - min) * 0.14;
  min -= padV; max += padV;

  const N = choices.length;
  const px = i => pad.l + (N <= 1 ? (W - pad.l - pad.r) / 2 : i * (W - pad.l - pad.r) / (N - 1));
  const py = v => pad.t + (1 - (v - min) / (max - min)) * (H - pad.t - pad.b);

  let svg = `<svg viewBox="0 0 ${W} ${H}" class="rd-chart-svg" preserveAspectRatio="xMidYMid meet">`;

  const TICKS = 5;
  for (let k = 0; k <= TICKS; k++) {
    const v = min + (max - min) * k / TICKS;
    const y = py(v);
    svg += `<line x1="${pad.l}" y1="${y.toFixed(1)}" x2="${W - pad.r}" y2="${y.toFixed(1)}" stroke="#242a38" stroke-width="1"/>`;
    svg += `<text x="${pad.l - 10}" y="${(y + 4).toFixed(1)}" text-anchor="end" class="rd-axis">${rdFmtNum(Math.round(v * 10) / 10)}</text>`;
  }
  if (min < 0 && max > 0) {
    const y0 = py(0);
    svg += `<line x1="${pad.l}" y1="${y0.toFixed(1)}" x2="${W - pad.r}" y2="${y0.toFixed(1)}" stroke="#4a5268" stroke-width="1.5" stroke-dasharray="5 5"/>`;
  }
  const step = Math.max(1, Math.ceil(N / 14));
  for (let i = 0; i < N; i += step) {
    svg += `<text x="${px(i).toFixed(1)}" y="${H - pad.b + 22}" text-anchor="middle" class="rd-axis">${i + 1}</text>`;
  }
  svg += `<text x="${((pad.l + W - pad.r) / 2).toFixed(1)}" y="${H - 8}" text-anchor="middle" class="rd-axis-title">Порядковый номер выбора игрока</text>`;

  names.forEach((n, idx) => {
    const color = RD_PALETTE[idx % RD_PALETTE.length];
    const pts = points[n];
    const d = pts.map((p, i) => `${i === 0 ? "M" : "L"}${px(p.i).toFixed(1)},${py(p.v).toFixed(1)}`).join(" ");
    svg += `<path d="${d}" fill="none" stroke="${color}" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round"/>`;
    pts.forEach(p => {
      svg += `<circle cx="${px(p.i).toFixed(1)}" cy="${py(p.v).toFixed(1)}" r="4.5" fill="${color}" stroke="#0e1016" stroke-width="2">`
           + `<title>${escapeHtml(n)}: ${rdFmtNum(p.v)} (выбор ${p.i + 1})</title></circle>`;
    });
  });

  svg += "</svg>";
  box.innerHTML = svg;

  legend.innerHTML = names.map((n, i) =>
    `<span class="rd-legend-item"><i style="background:${RD_PALETTE[i % RD_PALETTE.length]}"></i>${escapeHtml(n)}</span>`
  ).join("");
}

function rdRenderTotals(box, totals) {
  if (!totals.length) {
    box.innerHTML = `<p class="rd-muted">Итоговые параметры отсутствуют.</p>`;
    return;
  }
  const maxAbs = Math.max.apply(null, totals.map(t => Math.abs(t.totalAfter ?? t.value ?? 0)).concat([1]));

  box.innerHTML = totals.map(t => {
    const v = t.totalAfter ?? t.value ?? 0;
    const cls = v > 0 ? "pos" : v < 0 ? "neg" : "zero";
    const width = Math.min(50, Math.abs(v) / maxAbs * 50);
    const left = v >= 0 ? 50 : 50 - width;
    const sign = v > 0 ? "+" : "";
    return `
      <div class="rd-tot">
        <div class="rd-tot-head">
          <span class="rd-tot-name">${escapeHtml(t.name)}</span>
          <span class="rd-tot-val ${cls === "zero" ? "" : cls}">${sign}${rdFmtNum(v)}</span>
        </div>
        <div class="rd-tot-bar">
          <div class="zero"></div>
          <div class="fill ${cls === "zero" ? "" : cls}" style="left:${left}%;width:${width}%"></div>
        </div>
        <div class="rd-tot-sub">Изменение за сессию: ${(t.value > 0 ? "+" : "") + rdFmtNum(t.value ?? 0)}</div>
      </div>`;
  }).join("");
}

function rdRenderInsights(box, data, choices) {
  const dialogs = data.dialogs || [];
  const insights = [];
  const allText = choices.map(c => c.text || "").join("\n");
  const nChoices = choices.length || 1;

  const directiveCount = rdCount(allText, RD_RE.directive);
  const empathyCount   = rdCount(allText, RD_RE.empathy);
  const collabCount    = rdCount(allText, RD_RE.collab);

  const totals = rdGetTotals(data);
  totals.forEach(t => {
    if (/loyal|лояльн/i.test(t.name)) {
      const v = t.totalAfter ?? t.value;
      const isBad = v < 0;
      insights.push({
        level: isBad ? "gap" : "strength",
        title: isBad ? "Клиентоориентированность: просадка лояльности"
                     : "Клиентоориентированность: удержан баланс",
        text: `Итоговое значение «${t.name}» = ${rdFmtNum(v)}. ` +
              (isBad
                ? "Большинство выборов ухудшали отношение пассажиров — риск жалоб и эскалаций."
                : "Удалось сохранить или улучшить отношение пассажиров."),
        hint: isBad
          ? "Что тренировать: присоединение к эмоции («понимаю, что вы устали»), предложение варианта вместо запрета, снижение директивности."
          : "Продолжайте использовать формулу «признание эмоции → факт → вариант решения».",
      });
    }
    if (/security|безопасн/i.test(t.name)) {
      const v = t.totalAfter ?? t.value;
      insights.push({
        level: v > 0 ? "strength" : "warn",
        title: v > 0 ? "Безопасность и регламент: соблюдены"
                     : "Безопасность и регламент: под вопросом",
        text: `Итоговое значение «${t.name}» = ${rdFmtNum(v)}. ` +
              (v > 0
                ? "Регламент соблюдён, конфликтные ситуации переведены в контролируемое русло."
                : "Действия не обеспечили достаточного уровня безопасности в вагоне."),
        hint: v > 0
          ? "Сильная сторона: своевременное подключение охраны и фиксация нарушения."
          : "Что тренировать: своевременное подключение охраны, фиксация нарушения, работа по алгоритму.",
      });
    }
  });

  if (empathyCount === 0) {
    insights.push({
      level: "gap",
      title: "Эмпатия и активное слушание: не выражены",
      text: `Ни в одном из ${nChoices} выборов игрока нет вербального признания эмоций собеседника. ` +
            "Пассажир не получает сигнала, что его услышали, — это прямой триггер эскалации.",
      hint: "Что тренировать: техника присоединения — «Я понимаю, что ситуация неприятная», «Давайте разберёмся».",
      quotes: choices.slice(0, 2).map(c => c.text),
    });
  } else {
    insights.push({
      level: "strength",
      title: "Эмпатия: присутствует",
      text: `Найдено ${empathyCount} реплик(и) с признанием эмоций собеседника. Это снижает напряжение и повышает доверие.`,
      hint: "Усилить: добавлять эмпатию в первую реплику каждого конфликтного диалога.",
    });
  }

  if (collabCount === 0) {
    insights.push({
      level: "gap",
      title: "Конструктивность: нет предложенных решений",
      text: "Игрок сообщает о запретах и последствиях, но не предлагает пассажиру ни одного варианта действий.",
      hint: "Что тренировать: минимум одна альтернатива на каждый отказ («вызвать начальника поезда», «оформить акт», «пройти в тамбур»).",
    });
  } else {
    insights.push({
      level: collabCount >= nChoices * 0.5 ? "strength" : "warn",
      title: "Конструктивность: решения предлагаются",
      text: `Зафиксировано ${collabCount} реплик(и) с предложением варианта или привлечением третьей стороны.`,
      hint: "Усилить: предлагать альтернативу до озвучивания запрета, а не после.",
    });
  }

  if (directiveCount > 0) {
    const ratio = directiveCount / nChoices;
    insights.push({
      level: ratio >= 0.6 ? "gap" : "warn",
      title: "Директивный тон в коммуникации",
      text: `${directiveCount} реплик(и) содержат приказы, запреты и долженствования ` +
            "(«немедленно», «прекратите», «обязаны», «запрещено»). Такой тон повышает вероятность конфликта.",
      hint: "Что тренировать: замена «Вы обязаны» → «По правилам я могу предложить…», «Прекратите» → «Давайте снизим тон».",
      quotes: choices.filter(c => RD_RE.directive.test(c.text || "")).slice(0, 3).map(c => c.text),
    });
  }
  RD_RE.directive.lastIndex = 0;

  let escalations = 0;
  const escQuotes = [];
  dialogs.forEach(d => {
    const lines = d.lines || [];
    for (let i = 1; i < lines.length; i++) {
      const prev = lines[i - 1], cur = lines[i];
      if (rdIsPlayer(prev) && !rdIsPlayer(cur) && RD_RE.escalate.test(cur.text || "")) {
        escalations++;
        if (escQuotes.length < 3) escQuotes.push(cur.text);
      }
    }
  });
  if (escalations > 0) {
    insights.push({
      level: escalations >= 2 ? "gap" : "warn",
      title: "Эскалация конфликта после реплик игрока",
      text: `Зафиксировано ${escalations} случаев(я), когда ответ NPC после выбора игрока содержал угрозы, ` +
            "апелляцию к правам или требование жалобы.",
      hint: "Что тренировать: техника «снижение накала» — сначала признать право пассажира, затем объяснить ограничение.",
      quotes: escQuotes,
    });
  } else {
    insights.push({
      level: "strength",
      title: "Эскалация: удержана под контролем",
      text: "После выборов игрока не зафиксировано агрессивных ответных реакций NPC.",
      hint: "Хороший результат — сохраняйте текущую стратегию реагирования.",
    });
  }

  const questionCount = choices.filter(c => /\?/.test(c.text || "")).length;
  if (questionCount === 0 && choices.length > 0) {
    insights.push({
      level: "warn",
      title: "Диалог: отсутствуют уточняющие вопросы",
      text: "Игрок не задал ни одного вопроса — потребности и мотивы пассажира остаются невыясненными.",
      hint: "Что тренировать: уточняющие вопросы («Что именно произошло?», «Как вам удобнее поступить?»).",
    });
  }

  const gaps  = insights.filter(i => i.level === "gap").length;
  const warns = insights.filter(i => i.level === "warn").length;
  const good  = insights.filter(i => i.level === "strength").length;

  const summary = `
    <div class="rd-ins-summary">
      <span class="rd-pill gap">Пробелов: ${gaps}</span>
      <span class="rd-pill warn">Зон роста: ${warns}</span>
      <span class="rd-pill strong">Сильных сторон: ${good}</span>
    </div>`;

  const order = { gap: 0, warn: 1, strength: 2 };
  insights.sort((a, b) => order[a.level] - order[b.level]);

  const html = insights.map(i => `
    <div class="rd-ins ${i.level}">
      <div class="head"><span class="dot"></span><span class="title">${escapeHtml(i.title)}</span></div>
      <div class="txt">${escapeHtml(i.text)}</div>
      ${i.quotes && i.quotes.length ? `<div class="quotes">${i.quotes.map(q => `<div class="quote">«${escapeHtml(q)}»</div>`).join("")}</div>` : ""}
      ${i.hint ? `<div class="hintline"><b>Рекомендация:</b> ${escapeHtml(i.hint)}</div>` : ""}
    </div>`).join("");

  box.innerHTML = summary + html;
}

function rdParamChip(p) {
  const v = p.value ?? 0;
  const cls = v > 0 ? "pos" : v < 0 ? "neg" : "zero";
  const sign = v > 0 ? "+" : "";
  return `<span class="rd-chip ${cls}">${escapeHtml(p.name)} ${sign}${rdFmtNum(v)}<span class="arrow">→</span>${rdFmtNum(p.totalAfter)}</span>`;
}

function rdLineHtml(line) {
  const player = rdIsPlayer(line);
  const role = line.roleId || (player ? "Player" : "NPC");
  const initials = player ? "ВЫ" : (String(role).replace(/[^A-Za-z0-9]/g, "").slice(0, 4) || "NPC");
  const params = (line.parameters || []).map(rdParamChip).join("");

  return `
    <div class="rd-line ${player ? "player" : "npc"}">
      <div class="rd-avatar ${player ? "player" : "npc"}">${escapeHtml(initials)}</div>
      <div class="rd-bubble">
        <div class="meta">
          <span class="role">${escapeHtml(role)}</span>
          <span class="time">${escapeHtml(rdFmtTime(line.timestampUtc))}</span>
          ${line.lineId ? `<span class="lineid">${escapeHtml(line.lineId)}</span>` : ""}
        </div>
        <div class="text">${escapeHtml(line.text)}</div>
        ${params ? `<div class="chips">${params}</div>` : ""}
        ${line.targetLineId ? `<div class="target">→ ${escapeHtml(line.targetLineId)}</div>` : ""}
      </div>
    </div>`;
}

function rdDialogHtml(d, idx) {
  const lines = d.lines || [];
  const dur = rdFmtDur(d.startedAtUtc, d.endedAtUtc);
  const choiceCount = lines.filter(rdIsPlayer).length;

  const delta = {};
  lines.forEach(l => (l.parameters || []).forEach(p => {
    delta[p.name] = (delta[p.name] || 0) + (p.value || 0);
  }));
  const deltaChips = Object.keys(delta).map(name => {
    const v = delta[name];
    const cls = v > 0 ? "pos" : v < 0 ? "neg" : "zero";
    return `<span class="rd-chip ${cls}">${escapeHtml(name)} ${v > 0 ? "+" : ""}${rdFmtNum(v)}</span>`;
  }).join("");

  return `
    <div class="rd-dialog">
      <div class="rd-dialog-head">
        <div class="rd-dialog-idx">${idx + 1}</div>
        <div>
          <div class="rd-dialog-name">${escapeHtml(d.dialogId || "—")}</div>
        </div>
        <div class="rd-dialog-meta">
          <span>👤 ${escapeHtml(d.speakerId || "—")}</span>
          <span>🕒 ${escapeHtml(rdFmtTime(d.startedAtUtc))} – ${escapeHtml(rdFmtTime(d.endedAtUtc))}</span>
          ${dur ? `<span>⏱ ${escapeHtml(dur)}</span>` : ""}
          <span>💬 ${choiceCount} выбор(а)</span>
        </div>
        ${deltaChips ? `<div class="rd-dialog-delta">${deltaChips}</div>` : ""}
      </div>
      <div class="rd-lines">${lines.map(rdLineHtml).join("")}</div>
    </div>`;
}

function rdRenderDialogs(box, dialogs) {
  if (!dialogs.length) {
    box.innerHTML = `<p class="rd-muted">Диалоги отсутствуют.</p>`;
    return;
  }
  box.innerHTML = dialogs.map(rdDialogHtml).join("");
}

/* =========================== PROFILE ============================ */
async function openProfile() {
  const box = $("#profile-content");
  box.innerHTML = `<div class="empty">Загрузка…</div>`;

  try {
    const me = await api(CONFIG.AUTH.me);
    if (me && typeof me === "object") {
      State.user = { ...(State.user || {}), ...me };
      saveUser(State.user);
      renderUserChips();
    }
  } catch {}

  let results = [];
  try { results = (await api(CONFIG.SCENARIOS.results)) || []; } catch {}
  State.myResults = results;

  renderProfile(results);
}

function renderProfile(results) {
  const u = State.user || {};
  const xp = extractXp(u);
  const level = levelFromXp(xp);
  const curLevelXp   = xpToReachLevel(level);
  const nextLevelXp  = xpToReachLevel(level + 1);
  const inLevel      = xp - curLevelXp;
  const needForLevel = Math.max(1, nextLevelXp - curLevelXp);
  const pct = Math.min(100, Math.max(0, Math.round(inLevel / needForLevel * 100)));

  const initials = ((u.first_name?.[0] || "") + (u.second_name?.[0] || "")).toUpperCase() || "??";
  const fullName = [u.second_name, u.first_name, u.third_name].filter(Boolean).join(" ") || "Игрок";

  const totalPlay = results.length;
  const bestTime = results.reduce((acc, r) => {
    const d = r.duration_playtime;
    if (typeof d !== "number") return acc;
    return acc === null || d < acc ? d : acc;
  }, null);
  const totalTime = results.reduce((a, r) => a + (r.duration_playtime || 0), 0);
  const distinctScenarios = new Set(results.map(r => r.scenario_uuid).filter(Boolean)).size;

  const box = $("#profile-content");
  box.innerHTML = `
    <div class="profile-card">
      <div class="profile-head">
        <div class="profile-avatar">${escapeHtml(initials)}</div>
        <div class="profile-info">
          <h3>${escapeHtml(fullName)}</h3>
          <div class="profile-email">${escapeHtml(u.email || "—")}</div>
        </div>
        <div class="level-badge">
          <div class="level-num">${level}</div>
          <div class="level-lbl">уровень</div>
        </div>
      </div>

      <div class="xp-block">
        <div class="xp-head">
          <span>Опыт</span>
          <span><b>${xp.toLocaleString("ru-RU")}</b> XP</span>
        </div>
        <div class="xp-bar"><i style="width:${pct}%"></i></div>
        <div class="xp-foot">
          <span>${inLevel.toLocaleString("ru-RU")} / ${needForLevel.toLocaleString("ru-RU")} до уровня ${level + 1}</span>
          <span>${pct}%</span>
        </div>
      </div>
    </div>

    <div class="profile-stats">
      <div class="profile-stat">
        <div class="k">Всего прохождений</div>
        <div class="v">${totalPlay}</div>
        <div class="s">завершённых сессий</div>
      </div>
      <div class="profile-stat">
        <div class="k">Пройдено сценариев</div>
        <div class="v">${distinctScenarios}</div>
        <div class="s">уникальных сценариев</div>
      </div>
      <div class="profile-stat">
        <div class="k">Лучшее время</div>
        <div class="v">${bestTime !== null ? fmtTime(bestTime) : "—"}</div>
        <div class="s">минимальное прохождение</div>
      </div>
      <div class="profile-stat">
        <div class="k">Общее время в игре</div>
        <div class="v">${fmtTime(totalTime)}</div>
        <div class="s">суммарно</div>
      </div>
    </div>

    <div class="profile-note">
      Уровень рассчитывается локально на основе опыта. Порог для уровня <b>N</b>:
      <code>100 × (N−1) × N / 2</code> XP.
    </div>
  `;
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
    GameBridge.setUnity(inst, CONFIG.UNITY_OBJECT);
    return inst;
  }).catch((err) => {
    State.unityLoading = null;
    loading.innerHTML =
      `<div style="color:var(--danger)">Ошибка загрузки: ${escapeHtml(String(err))}</div>`;
    throw err;
  });

  return State.unityLoading;
}

/**
 * Запуск сценария.
 * POST /scenarios/start больше не вызывается — серверная запись создаётся
 * только в момент finish. Всё, что нужно Unity, передаётся в OnScenarioStarted.
 */
async function startScenario(scenario) {
  State.activeScenario = scenario;
  $("#hud-scenario").textContent = scenario.name;
  $("#hud-timer").textContent = "00:00";

  switchToScreen("game");

  try {
    await ensureUnity();
  } catch (ex) {
    toast("Не удалось загрузить игру: " + ex.message, "err");
    return;
  }

  showGameStart(false);
  State.gameStartedAt = performance.now();
  startTimer();

  // Передаём Unity всё, что нужно для старта. Если Unity ещё не готова —
  // сообщение встанет в очередь GameBridge и уйдёт после unityReady().
  GameBridge.send("OnScenarioStarted", {
    scenario_uuid: scenario.scenario_uuid,
    scenario_name: scenario.name,
    scenario_description: scenario.description || null,
    user: State.user ? {
      user_uuid:   State.user.user_uuid,
      email:       State.user.email,
      first_name:  State.user.first_name,
      second_name: State.user.second_name,
      third_name:  State.user.third_name || null,
      level:       levelFromXp(extractXp(State.user)),
      xp:          extractXp(State.user),
    } : null,
  });
}

/**
 * Возврат к списку сценариев: прячем игровое полотно, показываем overlay
 * со сценариями. Используется и при выходе игрока, и после finish.
 */
function returnToScenarioList() {
  stopTimer();
  showGameStart(true);
  switchToScreen("game");
}

/** Игрок нажал «Сменить сценарий» — уведомляем Unity и возвращаемся в меню. */
function leaveGame() {
  GameBridge.send("OnScenarioStop", {
    reason: "user_exit",
    scenario_uuid: State.activeScenario?.scenario_uuid || null,
  });
  resetGame();
  returnToScenarioList();
}

function resetGame() {
  stopTimer();
  State.activeScenario = null;
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

/* ============ РЕГИСТРАЦИЯ ОБРАБОТЧИКОВ UNITY → FRONTEND ========= */
function registerBridgeHandlers() {
  /* --- Финал сценария: Unity → Frontend → API ---
   *  Unity в конце сценария вызывает из jslib GP_Finish(json),
   *  jslib парсит JSON и вызывает window.GamePlatform.finish(payload).
   *  Мы формируем тело из 4 полей и шлём POST /scenarios/finish.
   *  После успешного сохранения — возвращаем список сценариев.
   */
  GameBridge.on("finish", async (payload) => {
    let p = payload;
    if (typeof p === "string") {
      try { p = JSON.parse(p); }
      catch (e) { throw new Error("Некорректный JSON от Unity: " + e.message); }
    }
    if (!p || typeof p !== "object") throw new Error("Пустой payload");

    if (!State.activeScenario) throw new Error("Сценарий не запущен");

    const duration = p.duration_playtime ??
      Math.round((performance.now() - State.gameStartedAt) / 1000);

    // result_json приводим к строке — серверное ТЗ требует строку
    let resultJson = p.result_json ?? "";
    if (typeof resultJson !== "string") {
      try { resultJson = JSON.stringify(resultJson); } catch { resultJson = ""; }
    }

    const body = {
      passenger_loyality: Math.max(0, Math.round(Number(p.passenger_loyality) || 0)),
      security_rating:    Math.max(0, Math.round(Number(p.security_rating)    || 0)),
      duration_playtime:  Math.max(0, Math.round(Number(duration)             || 0)),
      result_json:        resultJson,
    };

    console.log("[GamePlatform] finish → API", body);

    let res;
    try {
      res = await api(CONFIG.SCENARIOS.finish, { method: "POST", body });
    } catch (e) {
      GameBridge.send("OnResultSaveFailed", { error: String(e?.message || e) });
      toast("Не удалось сохранить результат: " + e.message, "err");
      throw e;
    }

    stopTimer();
    toast("Результат сохранён", "ok");

    const finishedUuid = State.activeScenario?.scenario_uuid || null;

    // Сообщаем Unity об успехе, пока мост жив
    GameBridge.send("OnResultSaved", {
      scenario_uuid: finishedUuid,
      server_response: res,
    });

    // Сбрасываем кэш — при следующем заходе список прохождений перечитается
    State.myResults = [];

    // Прячем игру и показываем список сценариев для нового запуска
    resetGame();
    returnToScenarioList();

    return res;
  });

  /* --- Промежуточное обновление — только лог --- */
  GameBridge.on("updateProgress", (payload) => {
    let p = payload;
    if (typeof p === "string") { try { p = JSON.parse(p); } catch { return; } }
    if (!p || typeof p !== "object") return;
    console.log("[GamePlatform] progress:", p);
  });

  /* --- Управление от Unity --- */
  GameBridge.on("toScenarios", () => leaveGame());

  GameBridge.on("openLeaderboardByUuid", (uuid) => {
    const s = State.scenarios.find(x => x.scenario_uuid === uuid);
    if (s) openLeaderboard(s);
  });

  GameBridge.on("toast", (msg) => toast(String(msg || "")));

  /* --- Утилиты для Unity --- */
  GameBridge.on("state", () => ({
    user: State.user,
    scenario: State.activeScenario,
    unity_ready: GameBridge.isReady(),
  }));

  GameBridge.on("getMyResults", () => api(CONFIG.SCENARIOS.results));

  GameBridge.on("getUserResults", (userUuid) => {
    const path = CONFIG.SCENARIOS.resultsByUser.replace(
      "{user_uuid}", encodeURIComponent(userUuid)
    );
    return api(path);
  });

  GameBridge.on("openResults",       openResults);
  GameBridge.on("openResultDetail",  openResultDetail);
  GameBridge.on("openProfile",       openProfile);
  GameBridge.on("getLevel", () => levelFromXp(extractXp(State.user)));
  GameBridge.on("getXp",    () => extractXp(State.user));
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
  setAuthed(true);
  renderUserChips();
  switchToScreen("game");
  showGameStart(true);
  setLeaderboardHint();
  try {
    await loadScenarios();
  } catch (ex) {
    if (ex.status === 401) {
      handleUnauthorized();
    } else {
      const grid = $("#scenarios-grid");
      if (grid) grid.innerHTML =
        `<div class="empty" style="grid-column:1/-1">Ошибка загрузки: ${escapeHtml(ex.message)}</div>`;
    }
  }
}

function handleUnauthorized() {
  State.user = null;
  try { localStorage.removeItem(CONFIG.USER_KEY); } catch {}
  renderUserChips();
  setAuthed(false);
  switchToScreen("auth");
  switchAuthTab("login");
  toast("Сессия истекла, войдите заново", "err");
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
  GameBridge.reset();
  setAuthed(false);
  showGameStart(true);
  setLeaderboardHint();
  renderUserChips();
  resetAuthForms();
  switchToScreen("auth");
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

$$("#app-nav button").forEach(btn => {
  btn.addEventListener("click", () => {
    const target = btn.dataset.nav;
    switchToScreen(target);
    if (target === "results") openResults();
    if (target === "profile") openProfile();
  });
});

$("#results-refresh")?.addEventListener("click", openResults);
$("#result-back")?.addEventListener("click", () => {
  switchToScreen("results");
  openResults();
});

/* Регистрируем обработчики до того, как Unity начнёт их вызывать */
registerBridgeHandlers();

/* ------ bootstrap ------ */
(async function init() {
  State.user = loadUserFromStorage();
  const token = getToken();
  if (token && !isTokenExpired(token)) {
    await enterApp();
  } else {
    clearToken();
    setAuthed(false);
    switchToScreen("auth");
    switchAuthTab("login");
  }
  console.log("[GamePlatform] UI ready");
})();