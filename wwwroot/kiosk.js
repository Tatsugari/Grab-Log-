window.Kiosk = (function () {
    let port = null;
    let writer = null;
    let reader = null;
    let pendingForm = null;
 
    // 1. GLOBAL CAPTURE INTERCEPTORS (Blocks ALL form reloads on click)
    document.addEventListener('click', function (e) {
        const button = e.target.closest('.btn-borrow, .btn-return');
        if (button) {
            e.preventDefault();
            e.stopPropagation();
            handleToolButtonClick(button);
        }
    }, true); // 'true' = Capture phase (intercepts BEFORE browser form submit)
 
    document.addEventListener('submit', function (e) {
        const form = e.target.closest('.card form');
        if (form) {
            e.preventDefault();
            e.stopPropagation();
        }
    }, true);
 
    // 2. CONNECT TO ESP32 VIA WEB SERIAL
    async function connect() {
        const statusEl = document.getElementById('kiosk-status');
        try {
            port = await navigator.serial.requestPort();
            await port.open({ baudRate: 115200 });
 
            const textDecoder = new TextDecoderStream();
            port.readable.pipeTo(textDecoder.writable);
            reader = textDecoder.readable.getReader();
 
            const textEncoder = new TextEncoderStream();
            textEncoder.readable.pipeTo(port.writable);
            writer = textEncoder.writable.getWriter();
 
            if (statusEl) {
                statusEl.textContent = "Connected (Ready)";
                statusEl.style.color = "#22c55e"; // Green
            }
 
            listenToESP32();
        } catch (err) {
            console.error("Serial connection error:", err);
            if (statusEl) {
                statusEl.textContent = "Connection failed";
                statusEl.style.color = "#ef4444"; // Red
            }
        }
    }
 
    // 3. HANDLE BORROW / RETURN BUTTON CLICK
    function handleToolButtonClick(button) {
        if (!writer) {
            alert("Please click 'Connect Kiosk' on the top bar first!");
            return;
        }
 
        const form = button.closest('form');
        const card = button.closest('.card');
        const toolId = card ? card.getAttribute('data-tool-id') : 'TOOL';
        const isBorrow = button.classList.contains('btn-borrow');
        const command = isBorrow ? 'ARM_BORROW\n' : 'ARM_RETURN\n';
 
        pendingForm = form;
 
        // Send ARM command over USB to ESP32
        sendSerial(command);
 
        const infoEl = document.getElementById('kiosk-selected-tool');
        if (infoEl) {
            infoEl.textContent = `Kiosk Armed (${isBorrow ? 'BORROW' : 'RETURN'} ${toolId}) — Scan RFID Card Now!`;
            infoEl.style.color = "#ffcc00";
        }
    }
 
    async function sendSerial(cmd) {
        if (writer) {
            await writer.write(cmd);
            console.log("Sent to ESP32:", cmd.trim());
        }
    }
 
    // 4. LISTEN FOR ESP32 RFID SCAN RESULTS
    async function listenToESP32() {
        let buffer = "";
        while (true) {
            const { value, done } = await reader.read();
            if (done) break;
            if (value) {
                buffer += value;
                let lines = buffer.split('\n');
                buffer = lines.pop(); // Keep trailing incomplete line
 
                for (let line of lines) {
                    line = line.trim();
                    console.log("[ESP32]:", line);
 
                    // Success response from ESP32: "TX_SUCCESS:6A5E8CA5:Ednice:BORROW"
                    if (line.startsWith("TX_SUCCESS:")) {
                        const infoEl = document.getElementById('kiosk-selected-tool');
                        if (infoEl) {
                            infoEl.textContent = "RFID Verified! Logging to database...";
                            infoEl.style.color = "#22c55e";
                        }
 
                        if (pendingForm) {
                            submitFormViaAjax(pendingForm);
                            pendingForm = null;
                        }
                    } else if (line.startsWith("TX_DENIED")) {
                        const infoEl = document.getElementById('kiosk-selected-tool');
                        if (infoEl) {
                            infoEl.textContent = "Access Denied — Unregistered Card";
                            infoEl.style.color = "#ef4444";
                        }
                    }
                }
            }
        }
    }
 
    // 5. SUBMIT TO C# BACKEND VIA AJAX (No premature page reloads)
    async function submitFormViaAjax(form) {
        try {
            const formData = new FormData(form);
            const actionUrl = form.getAttribute('action') || window.location.href;
 
            const response = await fetch(actionUrl, {
                method: 'POST',
                body: formData,
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });
 
            if (response.ok) {
                // Refresh page after backend updates database
                window.location.reload();
            } else {
                alert("Database update failed.");
            }
        } catch (err) {
            console.error("AJAX submission error:", err);
            form.submit();
        }
    }
 
    return {
        connect: connect,
        setSelectedTool: function (id) { console.log("Selected tool:", id); }
    };
})();
