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
      if (label) {
        label.textContent = selectedToolId
          ? ("Selected: " + selectedToolId + " — scan RFID now")
          : "Click a tool card to select it, then scan RFID";
      }
    },

    connect: async function () {
      if (!("serial" in navigator)) {
        alert("Web Serial needs Chrome or Edge on the PC with the ESP32 USB cable.");
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

        setStatus("ESP32 connected — select a tool, then scan", true);
        listen();
      } catch (err) {
        console.error(err);
        setStatus("Connect failed: " + (err && err.message ? err.message : err), false);
      }
    },

    arm: async function (mode) {
      if (!writer) return;
      const command = mode === "BORROW" ? "ARM_BORROW\n" : "ARM_RETURN\n";
      await writer.write(command);
      setStatus("Armed " + mode + " — scan RFID…", true);
    }
  };

  async function listen() {
    while (port && reader) {
      let result;
      try {
        result = await reader.read();
      } catch (e) {
        setStatus("Serial read error", false);
        break;
      }
      const { value, done } = result;
      if (done) break;
      if (!value) continue;
      buffer += value;
      let idx;
      while ((idx = buffer.indexOf("\n")) >= 0) {
        const line = buffer.slice(0, idx).trim();
        buffer = buffer.slice(idx + 1);
        if (line) handleLine(line);
      }
      const t = buffer.trim();
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
    const parts = message.split(":");
    saveTransaction(parts[1] || "", parts[2] || "", (parts[3] || "").toUpperCase());
  }

  async function saveTransaction(uid, name, action) {
    if (!selectedToolId) {
      setStatus("Select a tool card first, then scan again", false);
      alert("Click a tool card first, then scan the RFID.");
      return;
    }
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
        setStatus(data.message || "OK", true);
        setTimeout(function () { window.location.reload(); }, 600);
      } else {
        setStatus(data.error || "Failed", false);
        alert(data.error || "Transaction failed");
      }
    } catch (e) {
      setStatus("API error: " + e, false);
    }
  }
})();
