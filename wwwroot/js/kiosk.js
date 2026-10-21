(function () {
  let port = null;
  let writer = null;
  let reader = null;
  let selectedToolId = null;
  let pendingAction = null;
  let pendingOwnerCard = null; // expected RFID for return
  let buffer = "";
  let listening = false;

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

  // mode: BORROW | RETURN ; ownerCard optional for RETURN
  async function armEsp(mode, ownerCard) {
    if (!writer) return false;
    try {
      var cmd;
      if (mode === "RETURN" && ownerCard) {
        cmd = "ARM_RETURN:" + ownerCard + "\n";
      } else if (mode === "RETURN") {
        cmd = "ARM_RETURN\n";
      } else {
        cmd = "ARM_BORROW\n";
      }
      await writer.write(cmd);
      console.log("Sent to ESP32:", cmd.trim());
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
      b.onclick = function (e) { e.stopPropagation(); e.preventDefault(); Kiosk.requestReturn(toolId); };
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

  // Fetch owner card UID for a tool (for ARM_RETURN:uid)
  async function getToolOwnerCard(toolId) {
    try {
      var res = await fetch("/api/kiosk/tool-owner?toolId=" + encodeURIComponent(toolId), {
        credentials: "same-origin"
      });
      if (!res.ok) return null;
      var data = await res.json();
      return data.borrowedCardUid || null;
    } catch (e) {
      return null;
    }
  }

  window.Kiosk = {
    setSelectedTool: function (id) {
      selectedToolId = id || null;
      var label = document.getElementById("kiosk-selected-tool");
      if (label) {
        label.textContent = selectedToolId
          ? ("Selected: " + selectedToolId + " — waiting for RFID scan")
          : "Connect Kiosk, then click Borrow/Return and scan RFID";
      }
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

        setStatus("ESP32 connected — click Borrow or Return, then scan card", true);
        listen();
      } catch (err) {
        console.error(err);
        setStatus("Connect failed: " + (err && err.message ? err.message : err), false);
        clearConnection();
      }
    },

    requestBorrow: async function (toolId) {
      if (!isConnected()) {
        alert("Connect Kiosk first. Borrow only completes after RFID scan.");
        setStatus("Connect Kiosk first", false);
        return;
      }
      selectedToolId = toolId;
      pendingAction = "BORROW";
      pendingOwnerCard = null;
      window.Kiosk.setSelectedTool(toolId);

      var ok = await armEsp("BORROW");
      if (ok) {
        setStatus("Borrow " + toolId + " — scan RFID card now", true);
      }
    },

    requestReturn: async function (toolId) {
      if (!isConnected()) {
        alert("Connect Kiosk first. Return only completes after RFID scan.");
        setStatus("Connect Kiosk first", false);
        return;
      }
      selectedToolId = toolId;
      pendingAction = "RETURN";
      window.Kiosk.setSelectedTool(toolId);

      // Get who borrowed (card UID) so ESP32 only accepts that card
      var ownerCard = await getToolOwnerCard(toolId);
      pendingOwnerCard = ownerCard;

      var ok = await armEsp("RETURN", ownerCard);
      if (ok) {
        setStatus(
          ownerCard
            ? ("Return " + toolId + " — only card " + ownerCard + " can unlock. Scan now.")
            : ("Return " + toolId + " — scan RFID card now"),
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
        setStatus("ESP32 disconnected — click Connect Kiosk again", false);
        clearConnection();
        break;
      }
      if (result.done) {
        setStatus("ESP32 disconnected — click Connect Kiosk again", false);
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

    // Denied responses from ESP32
    if (
      message.startsWith("TX_DENIED_WRONG_BORROWER") ||
      message.indexOf("DENIED_WRONG_BORROWER") >= 0
    ) {
      setStatus("Access denied — wrong RFID (not the borrower)", false);
      alert("Access denied.\n\nOnly the person who borrowed this tool can return it.\nWrong RFID card scanned.");
      return;
    }
    if (
      message.startsWith("TX_DENIED_UNREGISTERED") ||
      message.indexOf("DENIED_UNREGISTERED") >= 0
    ) {
      setStatus("Access denied — unregistered card", false);
      alert("Access denied.\n\nThis RFID card is not registered.");
      return;
    }
    if (
      message.startsWith("TX_DENIED_LOCKED") ||
      message.indexOf("DENIED_LOCKED") >= 0
    ) {
      setStatus("Access denied — locker locked", false);
      alert("Access denied.\n\nLocker is locked.");
      return;
    }
    if (message.startsWith("TX_DENIED")) {
      setStatus("Access denied", false);
      alert("Access denied.\n\n" + message);
      return;
    }

    if (!message.startsWith("TX_SUCCESS:")) return;

    var parts = message.split(":");
    var action = pendingAction || (parts[3] || "").toUpperCase();
    if (action !== "BORROW" && action !== "RETURN") {
      action = (parts[3] || "").toUpperCase();
    }
    if (!selectedToolId) {
      setStatus("Click Borrow or Return on a tool first, then scan", false);
      return;
    }
    postTransaction(selectedToolId, action, parts[1] || "", parts[2] || "");
  }

  window.addEventListener("beforeunload", function () {
    if (isConnected()) clearConnection();
  });
})();
