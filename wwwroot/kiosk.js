window.Kiosk = (function () {
    let port = null;
    let writer = null;
    let reader = null;
    let pendingForm = null;
 
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
            writer = textEncoder.readable.getWriter();
 
            if (statusEl) {
                statusEl.textContent = "Connected (Ready)";
                statusEl.style.color = "#22c55e"; // Green
            }
 
            // Intercept form button clicks so the page doesn't reload before scanning
            setupInterceptors();
            listenToESP32();
        } catch (err) {
            console.error("Serial connection error:", err);
            if (statusEl) {
                statusEl.textContent = "Connection failed";
                statusEl.style.color = "#ef4444"; // Red
            }
        }
    }
 
    function setupInterceptors() {
        // Intercept both BORROW TOOL and RETURN buttons
        document.querySelectorAll('.btn-borrow, .btn-return').forEach(button => {
            button.addEventListener('click', function (e) {
                // Prevent immediate page reload / ASP.NET form submit
                e.preventDefault();
                e.stopPropagation();
 
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
 
                // Send ARM_BORROW or ARM_RETURN command over USB to ESP32
                sendSerial(command);
 
                const infoEl = document.getElementById('kiosk-selected-tool');
                if (infoEl) {
                    infoEl.textContent = `Kiosk Armed (${isBorrow ? 'BORROW' : 'RETURN'} ${toolId}) — Scan RFID Card Now!`;
                    infoEl.style.color = "#ffcc00";
                }
            });
        });
    }
 
    function setSelectedTool(toolId) {
        console.log("Tool container selected:", toolId);
    }
 
    async function sendSerial(cmd) {
        if (writer) {
            await writer.write(cmd);
            console.log("Sent to ESP32:", cmd.trim());
        }
    }
 
    async function listenToESP32() {
        let buffer = "";
        while (true) {
            const { value, done } = await reader.read();
            if (done) break;
            if (value) {
                buffer += value;
                let lines = buffer.split('\n');
                buffer = lines.pop(); // Keep trailing incomplete buffer
 
                for (let line of lines) {
                    line = line.trim();
                    console.log("[ESP32]:", line);
 
                    // Received success string from ESP32: "TX_SUCCESS:6A5E8CA5:Ednice:BORROW"
                    if (line.startsWith("TX_SUCCESS:")) {
                        const infoEl = document.getElementById('kiosk-selected-tool');
                        if (infoEl) {
                            infoEl.textContent = "RFID Verified! Updating database...";
                            infoEl.style.color = "#22c55e";
                        }
 
                        if (pendingForm) {
                            // Automatically submit the Razor form to save to SQL Database & update UI
                            pendingForm.submit();
                            pendingForm = null;
                        } else {
                            alert("RFID Scanned! Click a Borrow or Return button to select a tool.");
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
 
    return {
        connect: connect,
        setSelectedTool: setSelectedTool
    };
})();
