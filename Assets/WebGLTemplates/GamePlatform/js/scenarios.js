import { api } from "./api.js";
import { CONFIG } from "./config.js";
import { State } from "./state.js";
import { $, escapeHtml } from "./ui.js";
import { startScenario } from "./game.js";
import { openLeaderboard } from "./leaderboard.js";

export async function loadScenarios() {
  const grid = $("#scenarios-grid");
  const empty = $("#scenarios-empty");
  grid.innerHTML = `<div class="empty" style="grid-column:1/-1">Загрузка…</div>`;
  empty.style.display = "none";

  const list = await api(CONFIG.SCENARIOS.list);
  State.scenarios = list || [];
  renderScenarios();
}

export function renderScenarios() {
  const grid = $("#scenarios-grid");
  const empty = $("#scenarios-empty");
  grid.innerHTML = "";

  if (!State.scenarios.length) {
    empty.style.display = "block";
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
      </div>
    `;
    card.querySelector(".play").addEventListener("click", () => startScenario(s));
    card.querySelector(".board").addEventListener("click", () => openLeaderboard(s));
    grid.appendChild(card);
  }
}