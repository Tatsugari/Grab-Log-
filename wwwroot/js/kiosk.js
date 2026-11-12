(function () {
  let port = null;
  let writer = null;
  let reader = null;
  let selectedToolId = null;
  let pendingAction = null;
  let pendingOwnerCard = null;
  let buffer = "";
  let listening = false;

  // Map tool IDs to servo slots (1, 2, 3)
  function slotForTool(toolId) {
    if (!toolId) return "1";
    var id = String(toolId).toUpperCase().trim();
    if (id === "TD-001" || id === "1") return "1";
    if (id === "TD-002" || id === "2") return "2";
    if (id === "TD-003" || id === "3") return "3";
    // fallback: last digit if any, else 1
    var m = id.match(/(\d+)/);
    if (m) {
      var n = parseInt(m[1], 10);
      if (n >= 1 && n <= 3) return String(n);
    }
    return "1";
  }

  function setStatus(text, ok) {
    var el = document.getElementById("kiosk-status");
    if (!el) return;
    el.textContent = text;
    el.style.color = ok === false ? "#ef4444" : ok === true ? "#22c55e" : "#8a8a8a";
  }

  function isConnected() {
    return !!(port && writer && reader);
  }

  function clearConnection() {
    listening = false;
    try { if (reader) reader.cancel(); } catch (e) {}
    try { if (writer) writer.close(); } catch (e) {}
    try { if (port) port.close(); } catch (e) {}
    reader = null;
    writer = null;
    port = null;
  }

  /**
   * Sanitize to clean uppercase hex RFID (8 chars typical for MIFARE).
   * Rejects short numeric IDs (e.g. "1") that are not real card UIDs.
   */
  function cleanRfid(val) {
    if (!val) return "";
    var s = String(val).replace(/[^0-9A-Fa-f]/g, "").toUpperCase();
    // Guard: ignore values that look like short numeric internal IDs
    if (s.length > 0 && s.length <= 2 && /^\d+$/.test(s)) return "";
    return s;
  }

  /**
   * Sends:
   *   ARM_BORROW:1\n
   *   ARM_BORROW:2\n
   *   ARM_RETURN:1\n
   *   ARM_RETURN:2:6A5E8CA5\n   (slot + optional owner card)
   */
  async function armEsp(mode, toolId, ownerCard) {
    if (!writer) return false;
    try {
      var slot = slotForTool(toolId);
      var cmd;
      if (mode === "RETURN") {
        var clean = cleanRfid(ownerCard);
        if (clean) {
          cmd = "ARM_RETURN:" + slot + ":" + clean + "\n";
        } else {
          cmd = "ARM_RETURN:" + slot + "\n";
        }
      } else {
        cmd = "ARM_BORROW:" + slot + "\n";
      }
      await writer.write(cmd);
      console.log("Sending to Serial:", cmd.trim());
      setStatus("Sent " + cmd.trim() + " → servo slot " + slot, true);
      return true;
    } catch (e) {
      console.error(e);
      setStatus("ESP32 write failed — Connect Kiosk again", false);
      clearConnection();
      return false;
    }
  }

  function updateCardAfterTx(toolId, action) {
    var card = document.querySelector('[data-tool-id="' + toolId + '"]');
    if (!card) {
      document.querySelectorAll(".card").forEach(function (c) {
        var idEl = c.querySelector(".card-id");
        if (idEl && idEl.textContent.trim() === toolId) card = c;
      });
    }
    if (!card) return;

    var badge = card.querySelector(".badge");
    var btnBorrow = card.querySelector(".btn-borrow");
    var btnReturn = card.querySelector(".btn-return");

    if (action === "BORROW") {
      if (badge) { badge.className = "badge inuse"; badge.textContent = "INUSE"; }
      if (btnBorrow) btnBorrow.remove();
      if (btnReturn) btnReturn.remove();
      var b = document.createElement("button");
      b.type = "button";
      b.className = "btn-return";
      b.style.cssText = "background:#ef4444;color:#fff;width:100%;border:none;border-radius:7px;padding:10px;font-weight:600;cursor:pointer;";
      b.textContent = "RETURN";
      // Prefer the RFID that just borrowed this tool so ARM_RETURN gets the real hex UID
      var justBorrowedRfid = (typeof pendingOwnerCard === "string" && pendingOwnerCard) ? pendingOwnerCard : "";
      b.onclick = function (e) { e.stopPropagation(); e.preventDefault(); Kiosk.requestReturn(toolId, justBorrowedRfid); };
      card.appendChild(b);
    } else if (action === "RETURN") {
      if (badge) { badge.className = "badge available"; badge.textContent = "AVAILABLE"; }
      if (btnReturn) btnReturn.remove();
      if (btnBorrow) btnBorrow.remove();
      var b2 = document.createElement("button");
      b2.type = "button";
      b2.className = "btn-borrow";
      b2.style.cssText = "width:100%;background:#ffcc00;color:#111;border:none;border-radius:7px;padding:10px;font-weight:600;cursor:pointer;";
      b2.textContent = "BORROW TOOL";
      b2.onclick = function (e) { e.stopPropagation(); e.preventDefault(); Kiosk.requestBorrow(toolId); };
      card.appendChild(b2);
    }
  }

  async function postTransaction(toolId, action, cardUID, userName) {
    try {
      var res = await fetch("/api/kiosk/log-transaction", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({
          cardUID: cardUID || "",
          userName: userName || "",
          actionType: action,
          toolId: toolId
        })
      });
      var data = await res.json().catch(function () { return {}; });
      if (res.ok && data.ok) {
        setStatus((data.message || "OK") + " — ESP32 still connected", true);
        pendingAction = null;
        pendingOwnerCard = null;
        updateCardAfterTx(toolId, action);
        return true;
      }
      var err = data.error || ("Failed (" + res.status + ")");
      setStatus(err, false);
      alert(err);
      return false;
    } catch (e) {
      setStatus("API error: " + e, false);
      return false;
    }
  }

  async function getToolOwnerCard(toolId) {
    try {
      var res = await fetch("/api/kiosk/tool-owner?toolId=" + encodeURIComponent(toolId), {
        credentials: "same-origin"
      });
      if (!res.ok) return null;
      var data = await res.json();
      // Always return cleaned hex; never a raw username / numeric id
      return cleanRfid(data.borrowedCardUid) || null;
    } catch (e) {
      return null;
    }
  }

  /**
   * Send CLEAR_ALARM / RESET to ESP32 over Web Serial.
   * ESP32 stops continuous buzzer, locks doors, resets scale baselines, returns to STATE_IDLE.
   */
  async function sendClearAlarm() {
    if (!writer) {
      setStatus("Connect Kiosk first to clear alarm", false);
      return false;
    }
    try {
      // Prefer CLEAR_ALARM; also send RESET for firmware that only listens for RESET
      await writer.write("CLEAR_ALARM\n");
      await writer.write("RESET\n");
      setStatus("Alarm kill sent — CLEAR_ALARM + RESET", true);
      return true;
    } catch (e) {
      console.error(e);
      setStatus("Failed to send CLEAR_ALARM — reconnect Kiosk", false);
      clearConnection();
      return false;
    }
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

    /** Admin: silence theft alarm / reset ESP32 state */
    clearAlarm: async function () {
      if (!isConnected()) {
        alert("Connect Kiosk first, then use Kill Alarm.");
        setStatus("Connect Kiosk first", false);
        return false;
      }
      var ok = await sendClearAlarm();
      if (ok) {
        alert("Alarm clear command sent to ESP32.\nBuzzer should stop and locker return to idle.");
      }
      return ok;
    },

    connect: async function () {
      if (!("serial" in navigator)) {
        alert("Use Chrome or Edge on the PC with the ESP32 USB cable.");
        return;
      }
      try {
        clearConnection();
        port = await navigator.serial.requestPort();
        await port.open({ baudRate: 115200 });

        var textDecoder = new TextDecoderStream();
        port.readable.pipeTo(textDecoder.writable).catch(function () {});
        reader = textDecoder.readable.getReader();

        var textEncoder = new TextEncoderStream();
        textEncoder.readable.pipeTo(port.writable).catch(function () {});
        writer = textEncoder.writable.getWriter();

        setStatus("ESP32 connected — each tool controls its own servo (1/2/3)", true);
        listen();
      } catch (err) {
        console.error(err);
        setStatus("Connect failed: " + (err && err.message ? err.message : err), false);
        clearConnection();
      }
    },

    requestBorrow: async function (toolId) {
      if (!isConnected()) {
        alert("Connect Kiosk first.");
        setStatus("Connect Kiosk first", false);
        return;
      }
      selectedToolId = toolId;
      pendingAction = "BORROW";
      pendingOwnerCard = null;
      window.Kiosk.setSelectedTool(toolId);

      var slot = slotForTool(toolId);
      var ok = await armEsp("BORROW", toolId, null);
      if (ok) {
        setStatus("Borrow " + toolId + " (servo " + slot + ") — scan RFID now", true);
      }
    },

    /**
     * Arm ESP32 for return.
     * @param {string} toolId
     * @param {string} [borrowerRfid] Optional known borrower RFID UID from the page (preferred).
     *                                Falls back to /api/kiosk/tool-owner if missing/invalid.
     */
    requestReturn: async function (toolId, borrowerRfid) {
      if (!isConnected()) {
        alert("Connect Kiosk first.");
        setStatus("Connect Kiosk first", false);
        return;
      }
      selectedToolId = toolId;
      pendingAction = "RETURN";
      window.Kiosk.setSelectedTool(toolId);

      // Prefer the RFID embedded in the button (actual 8-char hex UID).
      // Never use a DB user id, username, or email as the owner card.
      var ownerCard = cleanRfid(borrowerRfid);
      if (!ownerCard) {
        // Fallback: fetch from server (tools.borrowed_card_uid)
        ownerCard = cleanRfid(await getToolOwnerCard(toolId));
      }
      pendingOwnerCard = ownerCard || null;

      var slot = slotForTool(toolId);
      var ok = await armEsp("RETURN", toolId, ownerCard);
      if (ok) {
        setStatus(
          "Return " + toolId + " (servo " + slot + ")" +
          (ownerCard ? " — only card " + ownerCard : " — any registered card") +
          " — scan RFID",
          true
        );
      }
    }
  };

  async function listen() {
    if (listening) return;
    listening = true;
    while (listening && reader) {
      var result;
      try {
        result = await reader.read();
      } catch (e) {
        setStatus("ESP32 disconnected — Connect Kiosk again", false);
        clearConnection();
        break;
      }
      if (result.done) {
        setStatus("ESP32 disconnected — Connect Kiosk again", false);
        clearConnection();
        break;
      }
      if (!result.value) continue;
      buffer += result.value;
      var idx;
      while ((idx = buffer.indexOf("\n")) >= 0) {
        var line = buffer.slice(0, idx).trim();
        buffer = buffer.slice(idx + 1);
        if (line) handleLine(line);
      }
      var t = buffer.trim();
      if (
        t.startsWith("TX_SUCCESS:") ||
        t.startsWith("TX_DENIED") ||
        t.startsWith("TX_ERROR")
      ) {
        handleLine(t);
        buffer = "";
      }
    }
  }

  function handleLine(message) {
    console.log("ESP32:", message);
    setStatus("ESP32: " + message, true);

    if (
      message.startsWith("TX_DENIED_WRONG_BORROWER") ||
      message.indexOf("DENIED_WRONG_BORROWER") >= 0
    ) {
      setStatus("Access denied — wrong RFID", false);
      alert("Access denied.\nOnly the person who borrowed this tool can return it.");
      return;
    }
    if (message.startsWith("TX_DENIED_UNREGISTERED") || message.indexOf("DENIED_UNREGISTERED") >= 0) {
      setStatus("Access denied — unregistered card", false);
      alert("Access denied. Unregistered RFID.");
      return;
    }
    if (message.startsWith("TX_DENIED_LOCKED") || message.indexOf("DENIED_LOCKED") >= 0) {
      setStatus("Access denied — locked", false);
      alert("Access denied. Locker locked.");
      return;
    }
    if (message.startsWith("TX_DENIED")) {
      setStatus("Access denied", false);
      alert("Access denied.\n" + message);
      return;
    }

    if (!message.startsWith("TX_SUCCESS:")) return;

    // Expected formats from ESP32:
    //   TX_SUCCESS:BORROW:TD-001:6A5E8CA5
    //   TX_SUCCESS:RETURN:TD-001:6A5E8CA5
    //   TX_SUCCESS:BORROW:6A5E8CA5          (older / minimal)
    //   TX_SUCCESS:RETURN:6A5E8CA5
    var parts = message.split(":");
    var action = (pendingAction || "").toUpperCase();
    var cardUID = "";
    var toolFromMsg = "";

    if (parts.length >= 4) {
      // TX_SUCCESS : ACTION : TOOLID : CARDUID
      var a = (parts[1] || "").toUpperCase();
      if (a === "BORROW" || a === "RETURN") action = a;
      toolFromMsg = (parts[2] || "").trim();
      cardUID = cleanRfid(parts[3] || "");
    } else if (parts.length >= 3) {
      // TX_SUCCESS : ACTION : CARDUID   or  TX_SUCCESS : TOOLID : CARDUID
      var mid = (parts[1] || "").toUpperCase();
      if (mid === "BORROW" || mid === "RETURN") {
        action = mid;
        cardUID = cleanRfid(parts[2] || "");
      } else {
        toolFromMsg = (parts[1] || "").trim();
        cardUID = cleanRfid(parts[2] || "");
      }
    } else if (parts.length >= 2) {
      cardUID = cleanRfid(parts[1] || "");
    }

    if (action !== "BORROW" && action !== "RETURN") {
      setStatus("Unknown TX action from ESP32: " + message, false);
      return;
    }
    if (!selectedToolId) {
      setStatus("Click Borrow or Return on a tool first, then scan", false);
      return;
    }
    // Prefer the tool the user clicked; fall back to tool id from the message if present
    var toolId = selectedToolId;
    if (toolFromMsg && /^TD-\d+/i.test(toolFromMsg)) {
      toolId = toolFromMsg.toUpperCase();
    }
    // Remember the card that just completed the action so a newly-created RETURN
    // button can pass the real hex UID (not a DB id / username) to ARM_RETURN.
    if (action === "BORROW" && cardUID) {
      pendingOwnerCard = cleanRfid(cardUID);
    }
    postTransaction(toolId, action, cardUID, "");
  }

  window.addEventListener("beforeunload", function () {
    if (isConnected()) clearConnection();
  });
})();
