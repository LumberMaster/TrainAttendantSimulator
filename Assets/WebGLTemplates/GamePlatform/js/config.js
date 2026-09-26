export const CONFIG = {
  API_BASE: "http://91.229.11.11:8001",

  AUTH: {
    login:    "/api/v1/auth/login",
    register: "/api/v1/auth/register",
    me:       "/api/v1/auth/me",       // опционально
  },

  SCENARIOS: {
    list:        "/api/v1/scenarios/",
    start:       "/api/v1/scenarios/start",
    finish:      "/api/v1/scenarios/finish",
    leaderboard: "/api/v1/scenarios/leaderboard",
  },

  TOKEN_KEY: "gp_token",
  USER_KEY:  "gp_user",
};