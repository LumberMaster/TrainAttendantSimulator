import { api } from "./api.js";
import { CONFIG } from "./config.js";
import { State } from "./state.js";
import { $, escapeHtml } from "./ui.js";
import { saveToken, clearToken, getToken, isTokenExpired } from "./token.js";

export function renderUserChips() {
  const u = State.user;
  const txt = u
    ? `<b>${escapeHtml(u.first_name || "")} ${escapeHtml(u.second_name || "")}</b> · ${escapeHtml(u.email || "")}`
    : "";
  const a = $("#user-info"); if (a) a.innerHTML = txt;
  const b = $("#lb-user");   if (b) b.innerHTML = txt;
}

function saveUser(user) {
  try {
    if (user) localStorage.setItem(CONFIG.USER_KEY, JSON.stringify(user));
    else localStorage.removeItem(CONFIG.USER_KEY);
  } catch {}
}

export function loadUserFromStorage() {
  try {
    const raw = localStorage.getItem(CONFIG.USER_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}

export function resetAuthForms() {
  ["form-login", "form-register"].forEach(id => {
    const f = document.getElementById(id);
    if (f) f.reset();
  });
  const le = $("#login-err");    if (le) le.textContent = "";
  const re = $("#register-err"); if (re) re.textContent = "";
}

/** Тянет токен из ответа, поддерживая разные имена поля. */
function extractToken(resp) {
  return resp?.access_token
      || resp?.accessToken
      || resp?.token
      || resp?.jwt
      || null;
}

/** Тянет юзера из ответа: либо resp.user, либо «остаток» корневого объекта. */
function extractUser(resp, fallback) {
  if (resp?.user && typeof resp.user === "object") return resp.user;
  if (resp && typeof resp === "object") {
    const { access_token, accessToken, token, jwt, token_type, expires_in, ...rest } = resp;
    if (rest && (rest.user_uuid || rest.email || rest.id)) return rest;
  }
  return fallback || null;
}

export async function login(email, password) {
  const resp = await api(CONFIG.AUTH.login, {
    method: "POST",
    body: { email, password },
    auth: false,
  });
  const token = extractToken(resp);
  if (!token) throw new Error("Сервер не вернул токен");
  saveToken(token);
  State.user = extractUser(resp, { email });
  saveUser(State.user);
  return State.user;
}

export async function register(payload) {
  const resp = await api(CONFIG.AUTH.register, {
    method: "POST",
    body: payload,
    auth: false,
  });
  const token = extractToken(resp);
  if (!token) throw new Error("Сервер не вернул токен");
  saveToken(token);
  State.user = extractUser(resp, {
    email: payload.email,
    first_name: payload.first_name,
    second_name: payload.second_name,
  });
  saveUser(State.user);
  return State.user;
}

/** Опционально: проверка валидности токена через /me (если эндпоинт есть). */
export async function fetchCurrentUser() {
  const t = getToken();
  if (!t || isTokenExpired(t)) return null;
  try {
    return await api(CONFIG.AUTH.me);
  } catch (ex) {
    if (ex.status === 401) return null;
    throw ex;
  }
}

export function logout() {
  clearToken();
  saveUser(null);
  State.user = null;
}