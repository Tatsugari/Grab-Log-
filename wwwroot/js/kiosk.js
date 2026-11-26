(function () {
  let port = null;
  let writer = null;
  let reader = null;
  let selectedToolId = null;
  let pendingAction = null;
  let pendingOwnerCard = null;
  let buffer = "";
  let listening = false;
  let lastTxKey = "";
  let lastTxAt = 0;
  let isAlarmActive = false;
  let isSystemLocked = false;
  let isTransactionBusy = false;
  let alarmStartedAt = null;
  let alarmToolId = null;
  let alarmKind = "THEFT"; // THEFT | DOOR
  let writeQueue = Promise.resolve();
  let heartbeatTimer = null;
  let armTimeout = null;
  let lastSerialActivity = 0;
  let reconnecting = false;
  let statusPollTimer = null;
  let lastScannedUid = "";
  let lastScanTimestamp = 0;

  const TOOL_NAMES = { "TD-001": "Pliers", "TD-002": "Screwdriver", "TD-003": "Hammer" };
  const ARM_TIMEOUT_MS = 15000;
  const HEARTBEAT_MS = 5000;
  const STATUS_POLL_MS = 4000;

  function slotForTool(toolId) {
    if (!toolId) return "1";
    var id = String(toolId).toUpperCase().trim();
    if (id === "TD-001" || id === "1") return "1";
    if (id === "TD-002" || id === "2") return "2";
    if (id === "TD-003" || id === "3") return "3";
    var m = id.match(/(\d+)/);
    if (m) {
      var n = parseInt(m[1], 10);
      if (n >= 1 && n <= 3) return String(n);
    }
    return "1";
  }

  function toolIdFromSlotOrCode(raw) {
    if (!raw) return "";
    var s = String(raw).trim().toUpperCase();
    if (s === "1" || s === "SLOT1") return "TD-001";
    if (s === "2" || s === "SLOT2") return "TD-002";
    if (s === "3" || s === "SLOT3") return "TD-003";
    if (/^TD-\d+/.test(s)) return s;
    return s;
  }

  function setStatus(text, ok) {
    var el = document.getElementById("kiosk-status");
    if (!el) return;
    el.textContent = text;
    el.style.color = ok === false ? "#ef4444" : ok === true ? "#22c55e" : "#8a8a8a";
  }

  function setConnBanner(text, disconnected) {
    var el = document.getElementById("kiosk-conn-banner");
    if (!el) {
      el = document.createElement("div");
      el.id = "kiosk-conn-banner";
      el.style.cssText = "display:none;width:100%;text-align:center;padding:8px 12px;font-size:12px;font-weight:700;border-radius:8px;margin-bottom:10px;";
      var bar = document.getElementById("kiosk-bar");
      if (bar && bar.parentNode) bar.parentNode.insertBefore(el, bar);
    }
    if (!text) {
      el.style.display = "none";
      return;
    }
    el.style.display = "block";
    el.textContent = text;
    el.style.background = disconnected ? "rgba(239,68,68,0.2)" : "rgba(34,197,94,0.15)";
    el.style.color = disconnected ? "#ef4444" : "#22c55e";
    el.style.border = "1px solid " + (disconnected ? "#ef4444" : "#22c55e");
  }

  function isConnected() {
    return !!(port && writer && reader);
  }

  function clearConnection() {
    listening = false;
    stopHeartbeat();
    try { if (reader) reader.cancel(); } catch (e) {}
    try { if (writer) writer.close(); } catch (e) {}
    try { if (port) port.close(); } catch (e) {}
    reader = null;
    writer = null;
    port = null;
  }

  function cleanRfid(val) {
    if (!val) return "";
    var s = String(val).replace(/[^0-9A-Fa-f]/g, "").toUpperCase();
    if (s.length > 0 && s.length <= 2 && /^\d+$/.test(s)) return "";
    return s;
  }

  /** Sequential write queue — never collide on port.writable */
  function sendSerialCommand(cmd) {
    writeQueue = writeQueue.then(async function () {
      if (!writer) throw new Error("No serial writer");
      try {
        var line = cmd.endsWith("\n") ? cmd : cmd + "\n";
        await writer.write(line);
        lastSerialActivity = Date.now();
        console.log("[SERIAL TX]", line.trim());
      } catch (e) {
        console.warn("[SERIAL TX] write glitch:", e);
        setStatus("Serial write warning — check cable", false);
        throw e;
      }
    }).catch(function (e) {
      // keep queue alive after error — do NOT close port
      console.warn("[SERIAL TX] queue error:", e && e.message ? e.message : e);
    });
    return writeQueue;
  }

  /**
   * Full idle reset after CLEAR_ALARM / STATUS:ALARM_CLEARED_* .
   * Does NOT touch Web Serial port/reader/writer.
   */
  function resetToIdle(reason) {
    console.log("[KIOSK] resetToIdle:", reason || "manual");
    isAlarmActive = false;
    isSystemLocked = false;
    isTransactionBusy = false;
    pendingAction = null;
    pendingOwnerCard = null;
    selectedToolId = null;
    lastTxKey = "";
    lastTxAt = 0;
    lastScannedUid = "";
    lastScanTimestamp = 0;
    clearArmTimeout();

    // Hide breach overlay
    var modal = document.getElementById("security-breach-modal");
    if (modal) modal.style.display = "none";

    // Clear ALL BREACHED badges (not only the last alarmToolId)
    document.querySelectorAll(".card .badge, [data-tool-id] .badge").forEach(function (badge) {
      if (badge.textContent === "BREACHED") {
        badge.className = badge.dataset.prevClass || "badge available";
        badge.style.background = "";
        badge.style.color = "";
        badge.textContent = badge.dataset.prevText || "AVAILABLE";
      }
    });
    alarmToolId = null;
    alarmStartedAt = null;
    alarmKind = "THEFT";

    unlockAllButtons();
    // Force-enable every borrow/return button (in case lock state was stuck)
    document.querySelectorAll(".btn-borrow, .btn-return, .card button").forEach(function (b) {
      b.disabled = false;
      b.style.opacity = "";
      delete b.dataset.wasLocked;
    });

    var label = document.getElementById("kiosk-selected-tool");
    if (label) label.textContent = "Connect Kiosk, then click Borrow/Return and scan RFID";
    setStatus("System idle — ready for borrow/return", true);
  }

  async function armEsp(mode, toolId, ownerCard) {
    if (!writer) return false;
    try {
      var slot = slotForTool(toolId);
      var cmd;
      if (mode === "RETURN") {
        var clean = cleanRfid(ownerCard);
        if (!clean) {
          setStatus("Cannot arm return — missing borrower RFID hex", false);
          alert("Cannot return: borrower RFID UID is missing.\nOnly the original borrower card can return this tool.");
          return false;
        }
        cmd = "ARM_RETURN:" + slot + ":" + clean + "\n";
      } else {
        cmd = "ARM_BORROW:" + slot + "\n";
      }
      await sendSerialCommand(cmd);
      setStatus("Sent " + cmd.trim() + " → servo slot " + slot, true);
      startArmTimeout();
      return true;
    } catch (e) {
      console.error(e);
      setStatus("ESP32 write failed — will try reconnect", false);
      scheduleReconnect();
      return false;
    }
  }

  function startArmTimeout() {
    clearArmTimeout();
    armTimeout = setTimeout(function () {
      if (pendingAction) {
        setStatus("Arm timeout — no card scanned in 15s. Click Borrow/Return again.", false);
        pendingAction = null;
        pendingOwnerCard = null;
        selectedToolId = null;
        isTransactionBusy = false;
        unlockAllButtons();
      }
    }, ARM_TIMEOUT_MS);
  }

  function clearArmTimeout() {
    if (armTimeout) {
      clearTimeout(armTimeout);
      armTimeout = null;
    }
  }

  function lockButton(toolId) {
    var card = document.querySelector('[data-tool-id="' + toolId + '"]');
    if (!card) return;
    card.querySelectorAll("button").forEach(function (b) {
      b.disabled = true;
      b.style.opacity = "0.6";
      b.dataset.wasLocked = "1";
    });
  }

  function unlockAllButtons() {
    document.querySelectorAll(".card button[data-was-locked]").forEach(function (b) {
      b.disabled = false;
      b.style.opacity = "";
      delete b.dataset.wasLocked;
    });
  }

  async function sendClearAlarm() {
    if (!writer) {
      setStatus("Connect Kiosk first to clear alarm", false);
      return false;
    }
    try {
      await sendSerialCommand("CLEAR_ALARM\n");
      await sendSerialCommand("RESET\n");
      setStatus("Alarm kill sent — CLEAR_ALARM + RESET", true);
      return true;
    } catch (e) {
      console.error(e);
      setStatus("Failed to send CLEAR_ALARM — reconnecting", false);
      scheduleReconnect();
      return false;
    }
  }

  function playSiren() {
    try {
      var ctx = new (window.AudioContext || window.webkitAudioContext)();
      function beep(freq, start, dur) {
        var o = ctx.createOscillator();
        var g = ctx.createGain();
        o.type = "square";
        o.frequency.value = freq;
        g.gain.value = 0.08;
        o.connect(g);
        g.connect(ctx.destination);
        o.start(ctx.currentTime + start);
        o.stop(ctx.currentTime + start + dur);
      }
      beep(880, 0, 0.2);
      beep(440, 0.22, 0.25);
      beep(880, 0.5, 0.2);
      setTimeout(function () { try { ctx.close(); } catch (e) {} }, 1200);
    } catch (e) {
      console.warn("Web Audio siren blocked:", e);
    }
  }

  function ensureBreachModal() {
    var el = document.getElementById("security-breach-modal");
    if (el) return el;
    el = document.createElement("div");
    el.id = "security-breach-modal";
    el.style.cssText = "display:none;position:fixed;inset:0;z-index:9999;background:rgba(120,0,0,0.92);color:#fff;align-items:center;justify-content:center;flex-direction:column;text-align:center;padding:24px;animation:breachPulse 1.2s ease-in-out infinite;";
    el.innerHTML =
      '<div style="font-size:12px;letter-spacing:2px;font-weight:700;background:#fff;color:#b91c1c;padding:6px 12px;border-radius:6px;margin-bottom:16px;">UNAUTHORIZED ACCESS DETECTED</div>' +
      '<div id="breach-title" style="font-size:42px;font-weight:800;margin-bottom:12px;">SECURITY BREACH</div>' +
      '<div id="breach-details" style="font-size:16px;max-width:520px;margin-bottom:8px;line-height:1.5;"></div>' +
      '<div id="breach-time" style="font-size:13px;opacity:0.85;margin-bottom:24px;"></div>' +
      '<button type="button" id="btn-reset-lock" style="background:#fff;color:#b91c1c;border:none;border-radius:8px;padding:14px 22px;font-size:15px;font-weight:800;cursor:pointer;">RESET &amp; LOCK SYSTEM</button>';
    document.body.appendChild(el);
    if (!document.getElementById("breach-pulse-style")) {
      var st = document.createElement("style");
      st.id = "breach-pulse-style";
      st.textContent = "@keyframes breachPulse{0%,100%{background:rgba(120,0,0,0.92)}50%{background:rgba(180,0,0,0.96)}}";
      document.head.appendChild(st);
    }
    document.getElementById("btn-reset-lock").onclick = function () {
      window.Kiosk.clearAlarm();
    };
    return el;
  }

  function markToolBreached(toolId, on) {
    if (!toolId) return;
    var cards = document.querySelectorAll("[data-tool-id], .card");
    cards.forEach(function (card) {
      var id = card.getAttribute("data-tool-id");
      if (!id) {
        var idEl = card.querySelector(".card-id");
        if (idEl) id = idEl.textContent.trim();
      }
      if (!id || id.toUpperCase() !== String(toolId).toUpperCase()) return;
      var badge = card.querySelector(".badge");
      if (!badge) return;
      if (on) {
        badge.dataset.prevClass = badge.className;
        badge.dataset.prevText = badge.textContent;
        badge.className = "badge";
        badge.style.background = "#ef4444";
        badge.style.color = "#fff";
        badge.textContent = "BREACHED";
      } else if (badge.textContent === "BREACHED") {
        badge.className = badge.dataset.prevClass || "badge available";
        badge.style.background = "";
        badge.style.color = "";
        badge.textContent = badge.dataset.prevText || "AVAILABLE";
      }
    });
  }

  function showBreach(kind, toolId) {
    var first = !isAlarmActive;
    isAlarmActive = true;
    isSystemLocked = true;
    alarmKind = kind || "THEFT";
    alarmToolId = toolId || alarmToolId || null;
    if (first) {
      alarmStartedAt = new Date();
      playSiren();
      logSecurityAlert(alarmKind, alarmToolId);
    }
    var modal = ensureBreachModal();
    var titleEl = document.getElementById("breach-title");
    var detailsEl = document.getElementById("breach-details");
    if (alarmKind === "DOOR") {
      if (titleEl) titleEl.textContent = "MAIN DOOR FORCED OPEN";
      if (detailsEl) detailsEl.textContent =
        "The main cabinet access door lost microswitch contact without an authorized return!";
    } else {
      var tid = alarmToolId || "TD-001";
      var slot = slotForTool(tid);
      var name = TOOL_NAMES[tid] || tid;
      if (titleEl) titleEl.textContent = "SECURITY BREACH";
      if (detailsEl) detailsEl.textContent =
        "Slot " + slot + " (" + name + " - " + tid + ") was removed or tampered with!";
      markToolBreached(tid, true);
    }
    document.getElementById("breach-time").textContent =
      "Detected: " + (alarmStartedAt ? alarmStartedAt.toLocaleString() : "");
    modal.style.display = "flex";
  }

  function hideBreach() {
    // Full state reset so RFID scans work immediately after CLEAR_ALARM
    resetToIdle("hideBreach");
  }

  async function logSecurityAlert(kind, toolId) {
    try {
      await fetch("/api/kiosk/security-alert", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({
          toolId: toolId || "",
          slot: toolId ? slotForTool(toolId) : "",
          toolName: toolId ? (TOOL_NAMES[toolId] || toolId) : "MAIN_DOOR",
          eventType: kind === "DOOR" ? "MAIN_DOOR_BREACH" : "THEFT_ALERT",
          timestamp: new Date().toISOString()
        })
      });
    } catch (e) {
      console.warn("security log failed", e);
    }
  }

  function updateCardAfterTx(toolId, action, cardUID) {
    var card = document.querySelector('[data-tool-id="' + toolId + '"]');
    if (!card) {
      document.querySelectorAll(".card").forEach(function (c) {
        var idEl = c.querySelector(".card-id");
        if (idEl && idEl.textContent.trim().toUpperCase() === String(toolId).toUpperCase()) card = c;
      });
    }
    if (!card) return;

    var badge = card.querySelector(".badge");
    var btnBorrow = card.querySelector(".btn-borrow");
    var btnReturn = card.querySelector(".btn-return");
    var note = card.querySelector(".borrowed-note");

    if (action === "BORROW") {
      if (badge) {
        badge.className = "badge inuse";
        badge.textContent = "INUSE";
        badge.style.background = "";
        badge.style.color = "";
      }
      if (btnBorrow) btnBorrow.remove();
      if (btnReturn) btnReturn.remove();
      if (note) note.remove();
      var b = document.createElement("button");
      b.type = "button";
      b.className = "btn-return";
      b.style.cssText = "background:#ef4444;color:#fff;width:100%;border:none;border-radius:7px;padding:10px;font-weight:600;cursor:pointer;";
      b.textContent = "RETURN";
      var uid = cleanRfid(cardUID || pendingOwnerCard || "");
      b.setAttribute("data-borrower-rfid", uid);
      b.onclick = function (e) {
        e.stopPropagation();
        e.preventDefault();
        Kiosk.requestReturn(toolId, uid);
      };
      card.appendChild(b);
    } else if (action === "RETURN") {
      if (badge) {
        badge.className = "badge available";
        badge.textContent = "AVAILABLE";
        badge.style.background = "";
        badge.style.color = "";
      }
      if (btnReturn) btnReturn.remove();
      if (btnBorrow) btnBorrow.remove();
      if (note) note.remove();
      // remove LOGGED line if present
      var meta = card.querySelector(".card-meta");
      if (meta) {
        meta.innerHTML = meta.innerHTML
          .replace(/<br\s*\/?>\s*<span[^>]*>LOGGED:.*?<\/span>/gi, "")
          .replace(/<br\s*\/?>\s*<span[^>]*>With:.*?<\/span>/gi, "");
      }
      var b2 = document.createElement("button");
      b2.type = "button";
      b2.className = "btn-borrow";
      b2.style.cssText = "width:100%;background:#ffcc00;color:#111;border:none;border-radius:7px;padding:10px;font-weight:600;cursor:pointer;";
      b2.textContent = "BORROW TOOL";
      b2.onclick = function (e) {
        e.stopPropagation();
        e.preventDefault();
        Kiosk.requestBorrow(toolId);
      };
      card.appendChild(b2);
    }
    unlockAllButtons();
  }

  async function postTransaction(toolId, action, cardUID, userName) {
    try {
      var res = await fetch("/api/kiosk/log-transaction", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({
          cardUID: cleanRfid(cardUID) || "",
          userName: userName || "",
          actionType: action,
          toolId: toolId
        })
      });
      var data = await res.json().catch(function () { return {}; });
      if (res.ok && data.ok) {
        setStatus((data.message || "OK") + " — ESP32 still connected", true);
        var uid = cleanRfid(cardUID);
        if (action === "BORROW" && uid) pendingOwnerCard = uid;
        updateCardAfterTx(toolId, action, uid);
        pendingAction = null;
        if (action === "RETURN") pendingOwnerCard = null;
        selectedToolId = null;
        clearArmTimeout();
        return true;
      }
      var err = data.error || ("Failed (" + res.status + ")");
      setStatus(err, false);
      alert(err);
      unlockAllButtons();
      return false;
    } catch (e) {
      console.error(e);
      setStatus("Network error saving transaction (serial still open)", false);
      unlockAllButtons();
      return false;
    }
  }

  async function getToolOwnerCard(toolId) {
    try {
      var res = await fetch("/api/kiosk/tool-owner?toolId=" + encodeURIComponent(toolId), {
        credentials: "same-origin"
      });
      if (!res.ok) return "";
      var data = await res.json();
      return cleanRfid(data.borrowedCardUid || "");
    } catch (e) {
      return "";
    }
  }

  function handleLine(message) {
    console.log("[SERIAL RX]", message);
    lastSerialActivity = Date.now();
    var upper = message.toUpperCase();

    // Heartbeat reply — never blocked by alarm flags
    if (upper === "PONG" || upper.indexOf("PONG") === 0) {
      setStatus("ESP32 connected (heartbeat OK)", true);
      setConnBanner("", false);
      return;
    }

    // Alarm cleared from hardware or web — full idle reset BEFORE any other guards
    if (
      upper.indexOf("STATUS:ALARM_CLEARED_BY_MICROSWITCH") >= 0 ||
      upper.indexOf("STATUS:ALARM_CLEARED_BY_WEB") >= 0 ||
      upper === "STATUS:ALARM_CLEARED" ||
      upper === "STATUS:IDLE" ||
      upper.indexOf("STATUS:RESET") >= 0
    ) {
      console.log("[KIOSK] alarm cleared message received");
      resetToIdle(message);
      return;
    }

    // Theft / door breach protocol
    if (upper.startsWith("ALERT:THEFT:")) {
      var tid = toolIdFromSlotOrCode(message.split(":")[2] || "");
      showBreach("THEFT", tid || "TD-001");
      setStatus("SECURITY BREACH " + (tid || ""), false);
      return;
    }
    if (
      upper.startsWith("ALERT:DOOR_BREACH") ||
      upper.startsWith("ALERT:MAIN_DOOR_OPEN") ||
      upper.indexOf("ALERT:DOOR") >= 0
    ) {
      showBreach("DOOR", null);
      setStatus("MAIN DOOR BREACH", false);
      return;
    }

    // RFID_SCANNED:<UID> — always normalize; never swallowed by stale isAlarmActive
    // (After CLEAR_ALARM, isAlarmActive is false; during alarm we still log the UID)
    if (upper.startsWith("RFID_SCANNED:") || upper.startsWith("RFID:")) {
      var rawUid = message.split(":").slice(1).join(":");
      var uid = cleanRfid(rawUid);
      console.log("[SERIAL RX] RFID_SCANNED normalized:", uid, "alarmActive=", isAlarmActive, "pending=", pendingAction);
      if (!uid) return;

      // Debounce same card only within 800ms (reset clears lastScannedUid)
      var nowScan = Date.now();
      if (uid === lastScannedUid && nowScan - lastScanTimestamp < 800) {
        console.log("[SERIAL RX] duplicate RFID ignored (debounce)");
        return;
      }
      lastScannedUid = uid;
      lastScanTimestamp = nowScan;

      if (isAlarmActive) {
        // Still acknowledge in console/status; do not block future post-reset scans
        setStatus("RFID " + uid + " (clear alarm first to arm)", false);
        return;
      }

      // If armed, keep waiting for TX_SUCCESS; status shows the tap was seen
      if (pendingAction) {
        setStatus("RFID " + uid + " seen — waiting for hardware TX_SUCCESS…", true);
      } else {
        setStatus("RFID " + uid + " — click Borrow/Return first", true);
      }
      return;
    }

    setStatus("ESP32: " + message, true);

    if (upper.indexOf("DENIED_WRONG_BORROWER") >= 0) {
      setStatus("Access denied — wrong RFID", false);
      alert("Access denied.\nOnly the person who borrowed this tool can return it.");
      clearArmTimeout();
      isTransactionBusy = false;
      unlockAllButtons();
      return;
    }
    if (upper.indexOf("DENIED_UNREGISTERED") >= 0) {
      setStatus("Access denied — unregistered RFID", false);
      alert("Access denied. Unregistered RFID.");
      clearArmTimeout();
      isTransactionBusy = false;
      unlockAllButtons();
      return;
    }
    if (upper.indexOf("DENIED_LOCKED") >= 0) {
      setStatus("Access denied — locked", false);
      alert("Access denied. Locker locked.");
      clearArmTimeout();
      isTransactionBusy = false;
      unlockAllButtons();
      return;
    }
    if (upper.startsWith("TX_DENIED")) {
      setStatus("Access denied", false);
      alert("Access denied.\n" + message);
      clearArmTimeout();
      isTransactionBusy = false;
      unlockAllButtons();
      return;
    }

    if (!upper.startsWith("TX_SUCCESS:")) return;

    // Deduplicate back-to-back identical TX_SUCCESS within 2s
    var now = Date.now();
    if (message === lastTxKey && now - lastTxAt < 2000) {
      console.log("[SERIAL RX] Ignoring duplicate TX_SUCCESS");
      return;
    }
    lastTxKey = message;
    lastTxAt = now;
    isTransactionBusy = false;

    // Formats:
    // TX_SUCCESS:RETURN:TD-001:6A5E8CA5
    // TX_SUCCESS:RETURN:1:6A5E8CA5
    // TX_SUCCESS:BORROW:TD-002:EEC36F9E
    // TX_SUCCESS:RETURN:6A5E8CA5
    var parts = message.split(":");
    var action = (pendingAction || "").toUpperCase();
    var cardUID = "";
    var toolFromMsg = "";

    if (parts.length >= 4) {
      var a = (parts[1] || "").toUpperCase();
      if (a === "BORROW" || a === "RETURN") action = a;
      toolFromMsg = toolIdFromSlotOrCode(parts[2] || "");
      cardUID = cleanRfid(parts[3] || "");
    } else if (parts.length >= 3) {
      var mid = (parts[1] || "").toUpperCase();
      if (mid === "BORROW" || mid === "RETURN") {
        action = mid;
        var p2 = parts[2] || "";
        if (/^TD-/i.test(p2) || /^[123]$/.test(p2.trim())) {
          toolFromMsg = toolIdFromSlotOrCode(p2);
        } else {
          cardUID = cleanRfid(p2);
        }
      } else {
        toolFromMsg = toolIdFromSlotOrCode(parts[1] || "");
        cardUID = cleanRfid(parts[2] || "");
      }
    } else if (parts.length >= 2) {
      cardUID = cleanRfid(parts[1] || "");
    }

    if (action !== "BORROW" && action !== "RETURN") {
      setStatus("Unknown TX action: " + message, false);
      return;
    }

    var toolId = selectedToolId;
    if (toolFromMsg && /^TD-\d+/i.test(toolFromMsg)) toolId = toolFromMsg.toUpperCase();
    if (!toolId) {
      setStatus("Click Borrow/Return on a tool first, then scan", false);
      return;
    }

    if (action === "BORROW" && cardUID) pendingOwnerCard = cardUID;
    clearArmTimeout();
    // AJAX only — never form POST / page reload (preserves Web Serial)
    postTransaction(toolId, action, cardUID, "");
  }

  async function listen() {
    if (listening) return;
    listening = true;
    while (listening && reader) {
      var result;
      try {
        result = await reader.read();
      } catch (e) {
        console.warn("Serial read error (non-fatal):", e);
        setStatus("Serial read glitch — reconnecting…", false);
        listening = false;
        scheduleReconnect();
        break;
      }
      if (result.done) {
        setStatus("ESP32 disconnected — reconnecting…", false);
        listening = false;
        scheduleReconnect();
        break;
      }
      if (!result.value) continue;
      lastSerialActivity = Date.now();
      buffer += result.value;
      var idx;
      while ((idx = buffer.indexOf("\n")) >= 0) {
        var line = buffer.slice(0, idx).trim();
        buffer = buffer.slice(idx + 1);
        if (line) {
          try { handleLine(line); } catch (he) { console.warn("handleLine error:", he); }
        }
      }
      var t = buffer.trim();
      if (
        t.startsWith("TX_SUCCESS:") ||
        t.startsWith("TX_DENIED") ||
        t.startsWith("ALERT:") ||
        t.startsWith("STATUS:ALARM") ||
        t === "PONG"
      ) {
        try { handleLine(t); } catch (he) { console.warn("handleLine error:", he); }
        buffer = "";
      }
    }
  }

  function startHeartbeat() {
    stopHeartbeat();
    lastSerialActivity = Date.now();
    heartbeatTimer = setInterval(async function () {
      if (!isConnected()) return;
      // silent drop detection
      if (Date.now() - lastSerialActivity > HEARTBEAT_MS * 3) {
        setStatus("No serial activity — possible disconnect", false);
        setConnBanner("KIOSK DISCONNECTED — attempting reconnect…", true);
        scheduleReconnect();
        return;
      }
      try {
        await sendSerialCommand("PING\n");
      } catch (e) {
        console.warn("Heartbeat failed:", e);
        scheduleReconnect();
      }
    }, HEARTBEAT_MS);
  }

  function stopHeartbeat() {
    if (heartbeatTimer) {
      clearInterval(heartbeatTimer);
      heartbeatTimer = null;
    }
  }

  async function tryOpenPort(p) {
    clearConnection();
    port = p;
    await port.open({ baudRate: 115200 });
    var textDecoder = new TextDecoderStream();
    port.readable.pipeTo(textDecoder.writable).catch(function () {});
    reader = textDecoder.readable.getReader();
    var textEncoder = new TextEncoderStream();
    textEncoder.readable.pipeTo(port.writable).catch(function () {});
    writer = textEncoder.writable.getWriter();
    lastSerialActivity = Date.now();
    setStatus("ESP32 connected — tools map to servos 1/2/3", true);
    setConnBanner("", false);
    startHeartbeat();
    listen();
  }

  function scheduleReconnect() {
    if (reconnecting) return;
    reconnecting = true;
    clearConnection();
    setConnBanner("KIOSK DISCONNECTED — attempting reconnect…", true);
    setTimeout(async function () {
      try {
        if (!("serial" in navigator)) return;
        var ports = await navigator.serial.getPorts();
        if (ports && ports.length > 0) {
          await tryOpenPort(ports[0]);
          setStatus("ESP32 auto-reconnected", true);
          setConnBanner("KIOSK RECONNECTED", false);
          setTimeout(function () { setConnBanner("", false); }, 2500);
        } else {
          setStatus("Kiosk disconnected — click Connect Kiosk", false);
        }
      } catch (e) {
        console.warn("Auto-reconnect failed:", e);
        setStatus("Reconnect failed — click Connect Kiosk", false);
      } finally {
        reconnecting = false;
      }
    }, 1200);
  }

  // USB unplug / browser disconnect event
  if ("serial" in navigator) {
    try {
      navigator.serial.addEventListener("disconnect", function () {
        setStatus("KIOSK DISCONNECTED", false);
        setConnBanner("KIOSK DISCONNECTED — USB removed or port lost", true);
        clearConnection();
        scheduleReconnect();
      });
    } catch (e) {}
  }

  // Lightweight status poll (source of truth, no page reload)
  function startStatusPoll() {
    if (statusPollTimer) return;
    statusPollTimer = setInterval(async function () {
      try {
        var res = await fetch("/api/kiosk/status", { credentials: "same-origin" });
        if (!res.ok) return;
        var data = await res.json();
        if (!data || !Array.isArray(data.tools)) return;
        data.tools.forEach(function (t) {
          var card = document.querySelector('[data-tool-id="' + t.id + '"]');
          if (!card) return;
          var badge = card.querySelector(".badge");
          if (!badge || badge.textContent === "BREACHED") return;
          var st = (t.status || "").toLowerCase();
          if (st === "available" && badge.textContent !== "AVAILABLE") {
            // only auto-sync if we are idle (no pending arm)
            if (!pendingAction) updateCardAfterTx(t.id, "RETURN", "");
          } else if ((st === "inuse" || st === "overdue") && badge.textContent === "AVAILABLE") {
            if (!pendingAction) updateCardAfterTx(t.id, "BORROW", t.borrowedCardUid || "");
          }
        });
      } catch (e) { /* quiet */ }
    }, STATUS_POLL_MS);
  }

  window.Kiosk = {
    setSelectedTool: function (id) {
      selectedToolId = id || null;
      var label = document.getElementById("kiosk-selected-tool");
      if (label) {
        label.textContent = selectedToolId
          ? ("Selected: " + selectedToolId + " → servo " + slotForTool(selectedToolId))
          : "Connect Kiosk, then click Borrow/Return and scan RFID";
      }
    },

    clearAlarm: async function () {
      // Always reset frontend state first so UI is idle even if serial write is slow
      resetToIdle("clearAlarm-clicked");
      if (!isConnected()) {
        alert("Connect Kiosk first, then use Kill Alarm / RESET.");
        setStatus("Connect Kiosk first", false);
        return false;
      }
      // Send CLEAR_ALARM / RESET over existing writer — do NOT close/reopen port
      var ok = await sendClearAlarm();
      // Keep serial reader alive; only re-assert idle after TX
      resetToIdle(ok ? "clearAlarm-sent" : "clearAlarm-send-failed");
      return ok;
    },

    connect: async function () {
      if (!("serial" in navigator)) {
        alert("Use Chrome or Edge on the PC with the ESP32 USB cable.");
        return;
      }
      try {
        clearConnection();
        var p = await navigator.serial.requestPort();
        await tryOpenPort(p);
        startStatusPoll();
      } catch (err) {
        console.error(err);
        setStatus("Connect failed: " + (err && err.message ? err.message : err), false);
        clearConnection();
      }
    },

    requestBorrow: async function (toolId) {
      if (isAlarmActive || isSystemLocked) {
        alert("Security breach active — reset alarm first.");
        return;
      }
      if (!isConnected()) {
        alert("Connect Kiosk first.");
        return;
      }
      // Fresh arm: clear scan debounce so same card works after reset
      lastScannedUid = "";
      lastScanTimestamp = 0;
      lastTxKey = "";
      lastTxAt = 0;
      selectedToolId = toolId;
      pendingAction = "BORROW";
      pendingOwnerCard = null;
      isTransactionBusy = true;
      window.Kiosk.setSelectedTool(toolId);
      lockButton(toolId);
      var ok = await armEsp("BORROW", toolId, null);
      if (ok) setStatus("Borrow " + toolId + " — scan RFID now (15s)", true);
      else {
        isTransactionBusy = false;
        unlockAllButtons();
      }
    },

    requestReturn: async function (toolId, borrowerRfid) {
      if (isAlarmActive || isSystemLocked) {
        alert("Security breach active — reset alarm first.");
        return;
      }
      if (!isConnected()) {
        alert("Connect Kiosk first.");
        return;
      }
      lastScannedUid = "";
      lastScanTimestamp = 0;
      lastTxKey = "";
      lastTxAt = 0;
      selectedToolId = toolId;
      pendingAction = "RETURN";
      window.Kiosk.setSelectedTool(toolId);

      var ownerCard = cleanRfid(borrowerRfid);
      if (!ownerCard) ownerCard = cleanRfid(await getToolOwnerCard(toolId));
      pendingOwnerCard = ownerCard || null;

      if (!ownerCard) {
        alert("Cannot return: no borrower RFID on file for this tool.");
        setStatus("Missing borrower RFID hex", false);
        return;
      }

      isTransactionBusy = true;
      lockButton(toolId);
      var ok = await armEsp("RETURN", toolId, ownerCard);
      if (ok) {
        setStatus("Return " + toolId + " — only card " + ownerCard + " — scan RFID (15s)", true);
      } else {
        isTransactionBusy = false;
        unlockAllButtons();
      }
    }
  };

  // Auto-try previously permitted ports on page load
  document.addEventListener("DOMContentLoaded", async function () {
    startStatusPoll();
    if (!("serial" in navigator)) return;
    try {
      var ports = await navigator.serial.getPorts();
      if (ports && ports.length > 0) {
        // soft-attempt only; user may still need to click Connect after permission
        setStatus("Previous kiosk port available — click Connect Kiosk", true);
      }
    } catch (e) {}
  });

  window.addEventListener("beforeunload", function () {
    stopHeartbeat();
    if (statusPollTimer) clearInterval(statusPollTimer);
    // do not force-close serial on unload if possible; browser will release it
  });
})();
