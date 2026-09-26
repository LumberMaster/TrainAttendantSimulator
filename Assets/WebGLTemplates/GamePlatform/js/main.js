import { $, showScreen, toast, setBusy, escapeHtml } from "./ui.js";
import {
  login, register, logout,
  renderUserChips, resetAuthForms,
  loadUserFromStorage,
} from "./auth.js";
import { loadScenarios } from "./scenarios.js";
import { leaveGame, resetGame, initGamePlatform } from "./game.js";
import { State } from "./state.js";
import { getToken, isTokenExpired, clearToken } from "./token.js";
import { CONFIG } from "./config.js";

/* ---------- Вкладки Вход/Регистрация ---------- */
function switchAuthTab(which) {
  const isLogin = which === "login";
  $("#tab-login")?.classList.toggle("active", isLogin);
  $("#tab-register")?.classList.toggle("active", !isLogin);
  const fl = $("#form-login");    if (fl) fl.style.display = isLogin ? "" : "none";
  const fr = $("#form-register"); if (fr) fr.style.display = isLogin ? "none" : "";
}
$("#tab-login")?.addEventListener("click",    () => switchAuthTab("login"));
$("#tab-register")?.addEventListener("click", () => switchAuthTab("register"));

/* ---------- Вход в приложение ---------- */
async function enterApp() {
  renderUserChips();
  showScreen("screen-scenarios");
  showScreen("screen-game");
  
  try {
    await loadScenarios();
  } catch (ex) {
    if (ex.status === 401) {
      // api.js уже почистил токен
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

/* ---------- Логин ---------- */
$("#form-login")?.addEventListener("submit", async (e) => {
  e.preventDefault();
  const err = $("#login-err"); if (err) err.textContent = "";
  const btn = $("#login-submit");
  setBusy(btn, true, "Вход…");
  try {
    await login($("#login-email").value.trim(), $("#login-pass").value);
    await enterApp();           // экран auth пропадает здесь
  } catch (ex) {
    if (err) err.textContent = "Ошибка: " + ex.message;
  } finally {
    setBusy(btn, false);
  }
});

/* ---------- Регистрация ---------- */
$("#form-register")?.addEventListener("submit", async (e) => {
  e.preventDefault();
  const err = $("#register-err"); if (err) err.textContent = "";
  const btn = $("#register-submit");
  setBusy(btn, true, "Создание…");
  try {
    const third = $("#reg-third").value.trim();
    await register({
      email:       $("#reg-email").value.trim(),
      password:    $("#reg-pass").value,
      first_name:  $("#reg-first").value.trim(),
      second_name: $("#reg-second").value.trim(),
      third_name:  third ? third : null,
    });
    await enterApp();
  } catch (ex) {
    if (err) err.textContent = "Ошибка: " + ex.message;
  } finally {
    setBusy(btn, false);
  }
});

/* ---------- Logout ---------- */
$("#btn-logout")?.addEventListener("click", () => {
  logout();
  resetGame();
  renderUserChips();
  resetAuthForms();
  showScreen("screen-auth");
  switchAuthTab("login");
  toast("Вы вышли из аккаунта");
});

/* ---------- Навигация ---------- */
$("#lb-back")?.addEventListener("click",   () => showScreen("screen-scenarios"));
$("#game-back")?.addEventListener("click", leaveGame);

/* ---------- Ресайз ---------- */
window.addEventListener("resize", () => {
  const c = $("#unity-canvas");
  if (c) { c.width = window.innerWidth; c.height = window.innerHeight; }
});

/* ---------- Unity bridge ---------- */
initGamePlatform();

/* ---------- Bootstrap ---------- */
(async function init() {
  // Восстанавливаем юзера из localStorage — только для отображения имени.
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