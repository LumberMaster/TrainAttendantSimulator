/* ============================================================
 *  GameBridge — мост между frontend (app.js) и Unity WebGL.
 *
 *  Направление Frontend → Unity:
 *      GameBridge.send("OnScenarioStarted", { ... })
 *      → unityInstance.SendMessage("GameBridge", "OnScenarioStarted", json)
 *
 *  Направление Unity → Frontend:
 *      Unity jslib вызывает window.GamePlatform.<method>(payload)
 *      → GameBridge._dispatch("method", payload)
 *      → зарегистрированный обработчик GameBridge.on("method", h)
 *
 *  Протокол готовности:
 *      Unity в GameBridge.Start() вызывает GamePlatform.unityReady().
 *      До этого момента все исходящие сообщения копятся в очереди
 *      и автоматически отправляются после готовности.
 * ============================================================ */

const DEFAULT_OBJECT_NAME = "GameBridge";

class GameBridgeClass {
  constructor() {
    /** @type {UnityInstance|null} */
    this._unity = null;
    this._objectName = DEFAULT_OBJECT_NAME;
    this._ready = false;
    /** @type {{method: string, payload: any}[]} */
    this._queue = [];
    /** @type {Map<string, Function>} */
    this._handlers = new Map();
    /** @type {Function[]} */
    this._readyHandlers = [];
    this._installGlobal();
  }

  /* ---------------- Публичный API ---------------- */

  /**
   * Зарегистрировать Unity instance, полученный из createUnityInstance().
   * Если Unity уже успела прислать unityReady() — очередь сразу прольётся.
   *
   * @param {UnityInstance} instance
   * @param {string} [objectName="GameBridge"] имя GameObject в сцене Unity
   */
  setUnity(instance, objectName) {
    this._unity = instance;
    if (objectName) this._objectName = objectName;
    if (this._ready) this._flush();
  }

  /** Переопределить имя GameObject. */
  setObjectName(name) {
    if (name) this._objectName = String(name);
  }

  /** Готов ли мост (Unity вызвала unityReady). */
  isReady() { return this._ready; }

  /** Подписаться на момент готовности Unity. */
  onReady(handler) {
    if (typeof handler !== "function") return;
    if (this._ready) {
      try { handler(); } catch (e) { console.error("[GameBridge] onReady handler:", e); }
    } else {
      this._readyHandlers.push(handler);
    }
  }

  /**
   * Отправить сообщение в Unity.
   * До готовности сообщение копится в очереди и уйдёт автоматически.
   *
   * @param {string} method  имя метода в GameBridge.cs
   * @param {any}    [payload] строка или объект (сериализуется в JSON)
   */
  send(method, payload) {
    if (!this._ready || !this._unity) {
      this._queue.push({ method, payload });
      return;
    }
    this._sendNow(method, payload);
  }

  /**
   * Зарегистрировать обработчик вызова от Unity.
   * Можно повесить несколько обработчиков на разные методы.
   *
   * @param {string} method
   * @param {Function} handler  async (payload) => any
   */
  on(method, handler) {
    if (typeof handler !== "function") {
      throw new TypeError(`GameBridge.on("${method}"): handler должен быть функцией`);
    }
    this._handlers.set(method, handler);
    return this;
  }

  /** Удалить обработчик. */
  off(method) {
    this._handlers.delete(method);
    return this;
  }

  /** Полный сброс (при logout, перезагрузке Unity и т.п.). */
  reset() {
    this._queue.length = 0;
    this._ready = false;
    this._readyHandlers.length = 0;
  }

  /* ---------------- Внутренние ---------------- */

  /**
   * Устанавливает window.GamePlatform, который вызывается из jslib.
   * Через Proxy любые обращения к GamePlatform.<method> перенаправляются
   * в _dispatch — не нужно вручную перечислять методы.
   */
  _installGlobal() {
    const self = this;

    window.GamePlatform = new Proxy(
      {
        // Спец-метод: Unity сообщает о готовности моста
        unityReady: function () { self.notifyReady(); },
      },
      {
        get(target, prop) {
          if (prop in target) return target[prop];
          // Универсальная диспетчеризация: любой другой метод идёт в реестр
          return (...args) => self._dispatch(prop, ...args);
        },
        has(target, prop) {
          return prop === "unityReady" || self._handlers.has(prop);
        },
      }
    );
  }

  /**
   * Вызывается из jslib (GP_UnityReady → GamePlatform.unityReady()).
   * Идемпотентно: повторные вызовы игнорируются.
   */
  notifyReady() {
    if (this._ready) return;
    this._ready = true;
    console.log("[GameBridge] Unity is ready");

    // Проливаем очередь → Unity
    this._flush();

    // Сообщаем подписчикам
    const handlers = this._readyHandlers.splice(0);
    for (const h of handlers) {
      try { h(); } catch (e) { console.error("[GameBridge] readyHandler:", e); }
    }
  }

  /** Отправка одного сообщения в Unity. */
  _sendNow(method, payload) {
    const str =
      payload == null       ? "" :
      typeof payload === "string" ? payload :
      JSON.stringify(payload);

    try {
      this._unity.SendMessage(this._objectName, method, str);
      console.log("[GameBridge → Unity]", method, str);
    } catch (e) {
      console.warn("[GameBridge] SendMessage failed:", method, e);
    }
  }

  /** Опустошить очередь исходящих сообщений. */
  _flush() {
    if (!this._unity) return;
    const pending = this._queue.splice(0);
    for (const { method, payload } of pending) this._sendNow(method, payload);
  }

  /**
   * Диспетчер вызовов от Unity.
   * payload может быть строкой (JSON) или уже объектом/массивом.
   */
  async _dispatch(method, ...args) {
    const handler = this._handlers.get(method);
    if (!handler) {
      console.warn(`[GameBridge] Нет обработчика для "${method}" от Unity`);
      return null;
    }
    try {
      return await handler(...args);
    } catch (e) {
      console.error(`[GameBridge] Обработчик "${method}" выбросил ошибку:`, e);
      return null;
    }
  }
}

export const GameBridge = new GameBridgeClass();