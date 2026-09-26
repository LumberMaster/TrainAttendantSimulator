import { CONFIG } from "./config.js";

export function saveToken(token) {
  try { localStorage.setItem(CONFIG.TOKEN_KEY, token); } catch {}
}

export function getToken() {
  try { return localStorage.getItem(CONFIG.TOKEN_KEY); } catch { return null; }
}

export function clearToken() {
  try { localStorage.removeItem(CONFIG.TOKEN_KEY); } catch {}
}

/** Декодирует payload JWT. Подпись НЕ проверяет — это делает сервер. */
export function decodeJwt(token) {
  if (!token) return null;
  const parts = token.split(".");
  if (parts.length !== 3) return null;
  try {
    const b64 = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const padded = b64 + "===".slice((b64.length + 3) % 4);
    const json = decodeURIComponent(
      atob(padded).split("").map(c =>
        "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2)
      ).join("")
    );
    return JSON.parse(json);
  } catch { return null; }
}

/** true, если exp в прошлом (с запасом 5 секунд). */
export function isTokenExpired(token) {
  const p = decodeJwt(token);
  if (!p || !p.exp) return false;  // сервер всё равно проверит
  return Date.now() >= p.exp * 1000 - 5000;
}