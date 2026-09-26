import { CONFIG } from "./config.js";
import { getToken, clearToken, isTokenExpired } from "./token.js";

/**
 * Обёртка над fetch.
 * auth: false — для запросов логина/регистрации (токена ещё нет).
 */
export async function api(path, { method = "GET", body = null, auth = true } = {}) {
  const url = (CONFIG.API_BASE || "") + path;

  const opts = {
    method,
    headers: { "Accept": "application/json" },
    // credentials: "include" БОЛЬШЕ НЕ НУЖНО — токен едет в заголовке
  };

  if (body !== null) {
    opts.headers["Content-Type"] = "application/json";
    opts.body = JSON.stringify(body);
  }

  if (auth) {
    const token = getToken();
    if (token && !isTokenExpired(token)) {
      opts.headers["Authorization"] = "Bearer " + token;
    }
  }

  const res = await fetch(url, opts);

  // Протух/невалиден токен — сносим его и бросаем 401.
  if (res.status === 401 && auth) {
    clearToken();
    const err = new Error("Сессия истекла");
    err.status = 401;
    throw err;
  }

  const text = await res.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }

  if (!res.ok) {
    const err = new Error(formatError(data) || `HTTP ${res.status}`);
    err.status = res.status;
    err.payload = data;
    throw err;
  }
  return data;
}

function formatError(payload) {
  if (!payload) return "";
  if (typeof payload === "string") return payload;
  if (Array.isArray(payload.detail)) {
    return payload.detail
      .map(e => `${(e.loc || []).slice(1).join(".")}: ${e.msg}`)
      .join("; ");
  }
  if (payload.detail) {
    return typeof payload.detail === "string"
      ? payload.detail
      : JSON.stringify(payload.detail);
  }
  return JSON.stringify(payload);
}