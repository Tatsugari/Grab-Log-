(function () {
  let port = null;
  let writer = null;
  let reader = null;
  let selectedToolId = null;
  let pendingAction = null;
  let buffer = "";
  let listening = false;

  function setStatus(text, ok) {
    const el = document.getElementById("kiosk-status");
    if (!el) return;
    el.textContent = text;
    el.style.color = ok === false ? "#ef4444" : ok === true ? "#22c55e" : "#8a8a8a";
  }

  function isConnected() {
    return !!(port && writer && reader);
  }

  async function armEsp(mode) {
    if (!writer) return false;
    try {
      const command = mode === "BORROW" ? "ARM_BORROW\n" : "ARM_RETURN\n";
      await writer.write(command);
      return true;
    } catch (e) {
      console.error("arm write failed", e);
      setStatus("ESP32 write failed — click Connect Kiosk again", false);
      clearConnection();
      return false;
    }
  }

  function clearConnection() {
    listening = false;
    try { reader && reader.cancel(); } catch (e) {}
    try { writer && writer.close(); } catch (e) {}
    try { port && port.close(); } catch (e) {}
    reader = null;
    writer = null;
    port = null;
  }

  // Update card UI without reloading the page (reload kills serial)
  function updateCardAfterTx(toolId, action) {
    var card = document.querySelector('[data-tool-id="' + toolId + '"]');
    if (!card) return;

    var badge = card.querySelector(".badge");
    var meta = card.querySelector(".card-meta");
    var btnBorrow = card.querySelector(".btn-borrow");
    var btnReturn = card.querySelector(".btn-return");
    var formBorrow = document.getElementById("form-borrow-" + toolId);
    var formReturn = document.getElementById("form-return-" + toolId);

    if (action === "BORROW") {
      if (badge) {
        badge.className = "badge inuse";
        badge.textContent = "INUSE";
      }
      if (btnBorrow) btnBorrow.remove();
      if (formBorrow) formBorrow.remove();
      if (!btnReturn) {
        var b = document.createElement("button");
        b.type = "button";
        b.className = "btn-return";
        b.style.cssText = "background:#ef4444;color:#fff;";
        b.textContent = "RETURN";
        b.onclick = function (e) {
          e.stopPropagation();
          Kiosk.requestReturn(toolId);
        };
        card.appendChild(b);
      }
    } else if (action === "RETURN") {
      if (badge) {
        badge.className = "badge available";
        badge.textContent = "AVAILABLE";
      }
      if (btnReturn) btnReturn.remove();
      if (formReturn) formReturn.remove();
      if (!btnBorrow) {
        var b2 = document.createElement("button");
        b2.type = "button";
        b2.className = "btn-borrow";
        b2.textContent = "BORROW TOOL";
        b2.onclick = function (e) {
          e.stopPropagation();
          Kiosk.requestBorrow(toolId);
        };
        card.appendChild(b2);
      }
    }
  }

  window.Kiosk = {
    setSelectedTool: function (id) {
      selectedToolId = id || null;
      var label = document.getElementById("kiosk-selected-tool");
      if (label) {
        label.textContent = selectedToolId
          ? ("Selected: " + selectedToolId)
          : "Connect Kiosk, then click Borrow/Return and scan RFID";
      }
    },

    connect: async function () {
      if (!("serial" in navigator)) {
        alert("Use Chrome or Edge on the PC with the ESP32 USB cable.");
        return;
      }
      try {
        // close old connection cleanly
        clearConnection();

        port = await navigator.serial.requestPort();
        await port.open({ baudRate: 115200 });

        const textDecoder = new TextDecoderStream();
        port.readable.pipeTo(textDecoder.writable).catch(function () {});
        reader = textDecoder.readable.getReader();

        const textEncoder = new TextEncoderStream();
        textEncoder.readable.pipeTo(port.writable).catch(function () {});
        writer = textEncoder.writable.getWriter();

        setStatus("ESP32 connected — click Borrow or Return, then scan", true);
        listen();
      } catch (err) {
        console.error(err);
        setStatus("Connect failed: " + (err && err.message ? err.message : err), false);
        clearConnection();
      }
    },

    requestBorrow: async function (toolId) {
      selectedToolId = toolId;
      pendingAction = "BORROW";
      window.Kiosk.setSelectedTool(toolId);

      if (!isConnected()) {
        // no ESP32: normal server borrow (will reload page — user must reconnect after)
        var f = document.getElementById("form-borrow-" + toolId);
        if (f) {
          setStatus("No kiosk — borrowing without RFID (reconnect after)", false);
          f.submit();
        } else {
          alert("Click Connect Kiosk first for RFID, or refresh the page.");
        }
        return;
      }

      var ok = await armEsp("BORROW");
      if (ok) setStatus("Borrow " + toolId + " — scan RFID now (stay on this page)", true);
    },

    requestReturn: async function (toolId) {
      selectedToolId = toolId;
      pendingAction = "RETURN";
      window.Kiosk.setSelectedTool(toolId);

      if (!isConnected()) {
        var f = document.getElementById("form-return-" + toolId);
        if (f) {
          setStatus("No kiosk — returning without RFID (reconnect after)", false);
          f.submit();
        } else {
          alert("Click Connect Kiosk first for RFID, or refresh the page.");
        }
        return;
      }

      var ok = await armEsp("RETURN");
      if (ok) setStatus("Return " + toolId + " — scan RFID now (stay on this page)", true);
    },

    arm: async function (mode) {
      if (!isConnected()) {
        setStatus("Not connected — click Connect Kiosk", false);
        return;
      }
      await armEsp(mode);
      setStatus("Armed " + mode + " — scan RFID…", true);
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
        console.error(e);
        setStatus("ESP32 disconnected — click Connect Kiosk again", false);
        clearConnection();
        break;
      }
      var value = result.value;
      var done = result.done;
      if (done) {
        setStatus("ESP32 disconnected — click Connect Kiosk again", false);
        clearConnection();
        break;
      }
      if (!value) continue;
      buffer += value;
      var idx;
      while ((idx = buffer.indexOf("\n")) >= 0) {
        var line = buffer.slice(0, idx).trim();
        buffer = buffer.slice(idx + 1);
        if (line) handleLine(line);
      }
      var t = buffer.trim();
      if (t.startsWith("TX_SUCCESS:") && t.split(":").length >= 4) {
        handleLine(t);
        buffer = "";
      }
    }
  }

  function handleLine(message) {
    console.log("ESP32:", message);
    setStatus("ESP32: " + message, true);
    if (!message.startsWith("TX_SUCCESS:")) return;
    var parts = message.split(":");
    var cardUID = parts[1] || "";
    var userName = parts[2] || "";
    var action = pendingAction || (parts[3] || "").toUpperCase();
    if (action !== "BORROW" && action !== "RETURN") {
      action = (parts[3] || "").toUpperCase();
    }
    saveTransaction(cardUID, userName, action);
  }

  async function saveTransaction(uid, name, action) {
    if (!selectedToolId) {
      setStatus("Click Borrow or Return on a tool first", false);
      return;
    }
    if (action !== "BORROW" && action !== "RETURN") {
      setStatus("Unknown action from RFID", false);
      return;
    }
    var toolId = selectedToolId;
    try {
      var res = await fetch("/api/kiosk/log-transaction", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({
          cardUID: uid,
          userName: name,
          actionType: action,
          toolId: toolId
        })
      });
      var data = await res.json().catch(function () { return {}; });
      if (res.ok && data.ok) {
        setStatus((data.message || "OK") + " — ESP32 still connected", true);
        pendingAction = null;
        // NO page reload — that was disconnecting the ESP32
        updateCardAfterTx(toolId, action);
      } else {
        setStatus(data.error || "Failed", false);
        alert(data.error || "Transaction failed");
      }
    } catch (e) {
      setStatus("API error: " + e, false);
    }
  }

  // Warn if user navigates away while connected
  window.addEventListener("beforeunload", function () {
    if (isConnected()) clearConnection();
  });
})();
