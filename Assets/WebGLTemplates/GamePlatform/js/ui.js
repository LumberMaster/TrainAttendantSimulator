export const $  = (sel) => document.querySelector(sel);
export const $$ = (sel) => Array.from(document.querySelectorAll(sel));

export function showScreen(id) {
  $$(".screen").forEach(s => s.classList.toggle("active", s.id === id));
}

let toastTimer = null;
export function toast(msg, kind = "") {
  const el = $("#toast");
  el.textContent = msg;
  el.className = "show " + kind;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.className = kind; }, 3200);
}

export function setBusy(btn, busy, busyText = "…") {
  if (!btn) return;
  if (busy) {
    btn.dataset._text = btn.textContent;
    btn.disabled = true;
    btn.innerHTML = `<span class="spinner"></span> ${busyText}`;
  } else {
    btn.disabled = false;
    btn.textContent = btn.dataset._text || btn.textContent;
  }
}

export function fmtTime(seconds) {
  seconds = Math.max(0, Math.floor(seconds));
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  const pad = n => String(n).padStart(2, "0");
  return h > 0 ? `${pad(h)}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}

export function fmtDate(iso) {
  if (!iso) return "—";
  const d = new Date(iso);
  if (isNaN(d)) return iso;
  return d.toLocaleString("ru-RU", { dateStyle: "short", timeStyle: "short" });
}

export function escapeHtml(s) {
  return String(s ?? "").replace(/[&<>"']/g, c => (
    { "&":"&amp;", "<":"&lt;", ">":"&gt;", '"':"&quot;", "'":"&#39;" }[c]
  ));
}