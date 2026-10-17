(function () {
  let port = null;
  let writer = null;
  let reader = null;
  let selectedToolId = null;
  let buffer = "";

  function setStatus(text, ok) {
    const el = document.getElementById("kiosk-status");
    if (!el) return;
    el.textContent = text;
    el.style.color = ok === false ? "#ef4444" : ok === true ? "#22c55e" : "#8a8a8a";
  }

  window.Kiosk = {
    setSelectedTool: function (id) {
      selectedToolId = id || null;
      const label = document.getElementById("kiosk-selected-tool");
      if (label) label.textContent = selectedToolId
        ? ("Selected: " + selectedToolId)
        : "No tool selected — scan tool RFID or click a tool card first";
    },

    connect: async function () {
      if (!("serial" in navigator)) {
        alert("Web Serial not supported. Use Chrome or Edge on the kiosk PC.");
        return;
      }
      try {
        port = await navigator.serial.requestPort();
        await port.open({ baudRate: 115200 });

        const textDecoder = new TextDecoderStream();
        port.readable.pipeTo(textDecoder.writable);
        reader = textDecoder.readable.getReader();

        const textEncoder = new TextEncoderStream();
        textEncoder.readable.pipeTo(port.writable);
        writer = textEncoder.writable.getWriter();

        setStatus("ESP32 Kiosk connected", true);
        listen();
      } catch (err) {
        console.error(err);
        setStatus("Connect failed: " + (err && err.message ? err.message : err), false);
      }
    },

    arm: async function (mode) {
      if (!writer) {
        alert("Click Connect Kiosk first.");
        return;
      }
      const command = mode === "BORROW" ? "ARM_BORROW\n" : "ARM_RETURN\n";
      await writer.write(command);
      setStatus("Armed for " + mode + " — scan RFID card…", true);
    }
  };

  async function listen() {
    while (port && reader) {
      const { value, done } = await reader.read();
      if (done) break;
      if (!value) continue;
      buffer += value;
      let idx;
      while ((idx = buffer.indexOf("\n")) >= 0) {
        const line = buffer.slice(0, idx).trim();
        buffer = buffer.slice(idx + 1);
        if (line) handleLine(line);
      }
    }
  }

  function handleLine(message) {
    console.log("ESP32:", message);
    setStatus("ESP32: " + message, true);
    if (!message.startsWith("TX_SUCCESS:")) return;
    const parts = message.split(":");
    saveTransaction(parts[1] || "", parts[2] || "", (parts[3] || "").toUpperCase());
  }

  async function saveTransaction(uid, name, action) {
    try {
      const res = await fetch("/api/kiosk/log-transaction", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({
          cardUID: uid,
          userName: name,
          actionType: action,
          toolId: selectedToolId
        })
      });
      const data = await res.json().catch(function () { return {}; });
      if (res.ok && data.ok) {
        setStatus(data.message || "Transaction OK", true);
        setTimeout(function () { window.location.reload(); }, 800);
      } else {
        setStatus(data.error || "Transaction failed", false);
        alert(data.error || "Transaction failed");
      }
    } catch (e) {
      setStatus("API error: " + e, false);
    }
  }
})();
