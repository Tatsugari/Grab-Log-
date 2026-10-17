window.Kiosk = (function () {
    let port, writer, reader;
    let activeFormToSubmit = null;
 
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
                statusEl.style.color = "#22c55e";
            }
 
            // Attach event listeners to all Borrow/Return buttons on the page
            setupFormInterceptors();
            listenToESP32();
        } catch (err) {
            console.error("Serial error:", err);
            if (statusEl) {
                statusEl.textContent = "Connection failed";
                statusEl.style.color = "#ef4444";
            }
        }
    }
 
    function setupFormInterceptors() {
        // Intercept all Borrow and Return form buttons
        document.querySelectorAll('.btn-borrow, .btn-return').forEach(button => {
            button.type = "button"; // Prevent immediate page reload/submit
            button.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
 
                const form = button.closest('form');
                activeFormToSubmit = form;
 
                const isBorrow = button.classList.contains('btn-borrow');
                const command = isBorrow ? 'ARM_BORROW' : 'ARM_RETURN';
 
                const toolEl = document.getElementById('kiosk-selected-tool');
                if (toolEl) {
                    toolEl.textContent = `Kiosk Armed (${isBorrow ? 'BORROW' : 'RETURN'}) - Scan RFID Card Now!`;
                    toolEl.style.color = "#ffcc00";
                }
 
                // Send ARM command to ESP32 over Serial
                armKiosk(command);
            });
        });
    }
 
    function setSelectedTool(toolId) {
        // Highlight tool selection on screen if needed
        console.log("Selected tool:", toolId);
    }
 
    async function armKiosk(command) {
        if (writer) {
            await writer.write(command + '\n');
            console.log("Sent to ESP32:", command);
        } else {
            alert("Please click 'Connect Kiosk' first!");
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
                buffer = lines.pop(); // keep buffer tail
 
                for (let line of lines) {
                    line = line.trim();
                    console.log("ESP32:", line);
 
                    // Received success signal from ESP32: "TX_SUCCESS:6A5E8CA5:Ednice:BORROW"
                    if (line.startsWith("TX_SUCCESS:")) {
                        if (activeFormToSubmit) {
                            const statusEl = document.getElementById('kiosk-status');
                            if (statusEl) statusEl.textContent = "Scan Verified! Submitting...";
 
                            // Now submit the ASP.NET Razor form to update SQL Database
                            activeFormToSubmit.submit();
                        } else {
                            alert("RFID Scanned, but no tool button was clicked first!");
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
