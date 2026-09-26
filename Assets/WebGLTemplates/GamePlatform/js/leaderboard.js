import { api } from "./api.js";
import { CONFIG } from "./config.js";
import { $, showScreen, escapeHtml, fmtTime, fmtDate } from "./ui.js";

export async function openLeaderboard(scenario) {
  showScreen("screen-leaderboard");
  $("#lb-title").textContent = "Рейтинг — " + scenario.name;

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
    box.innerHTML = `
      <div class="empty">
        Пока никто не прошёл этот сценарий.<br>
        <span style="font-size:12px;opacity:.6">Стань первым!</span>
      </div>`;
    return;
  }

  const rows = entries.map((e, i) => {
    const place = i + 1;
    const cls = place <= 3 ? ` class="p${place}"` : "";
    const loy = (e.passenger_loyality ?? null) !== null ? e.passenger_loyality : "—";
    const sec = (e.security_rating   ?? null) !== null ? e.security_rating   : "—";
    const end = e.time_end ? fmtDate(e.time_end) : "—";
    return `
      <tr${cls}>
        <td class="place">${place}</td>
        <td>${escapeHtml(e.first_name)} ${escapeHtml(e.second_name)}</td>
        <td class="num">${fmtTime(e.duration_playtime)}</td>
        <td class="num">${loy}</td>
        <td class="num">${sec}</td>
        <td style="color:var(--muted);font-size:12px">${end}</td>
      </tr>`;
  }).join("");

  box.innerHTML = `
    <div class="lb-head">
      <h2>Топ игроков по времени прохождения</h2>
    </div>
    <table class="lb-table">
      <thead><tr>
        <th>#</th><th>Игрок</th><th>Время</th>
        <th>Лояльность</th><th>Безопасность</th><th>Завершено</th>
      </tr></thead>
      <tbody>${rows}</tbody>
    </table>
  `;
}