interface DnsLog {
    id: number;
    timestamp: string;
    domain: string;
    clientIp: string;
    reason: 'Ads' | 'Malware' | 'Telemetry';
}

const logContainer = document.getElementById('logContainer')!;
const detailPanel = document.getElementById('detailPanel')!;
const detailDomain = document.getElementById('detailDomain')!;
const detailReason = document.getElementById('detailReason')!;
const detailTime = document.getElementById('detailTime')!;
const detailIp = document.getElementById('detailIp')!;
const btnWhitelist = document.getElementById('btnWhitelist') as HTMLButtonElement;

let selectedDomain = "";
// Endereço onde o C# vai estar rodando
const API_BASE = "http://localhost:5005/api";

async function fetchLogs() {
    try {
        const response = await fetch(`${API_BASE}/logs`);
        if (!response.ok) throw new Error("API Offline");
        const logs: DnsLog[] = await response.json();
        renderLogs(logs);
    } catch (error) {
        // Se o C# não responder (ainda não ligamos ele), carrega dados falsos pra testarmos a UI
        renderLogs([
            { id: 1, timestamp: new Date().toISOString(), domain: "google-analytics.com", clientIp: "192.168.0.10", reason: "Telemetry" },
            { id: 2, timestamp: new Date(Date.now() - 120000).toISOString(), domain: "minerador-cripto.net", clientIp: "192.168.0.15", reason: "Malware" },
            { id: 3, timestamp: new Date(Date.now() - 300000).toISOString(), domain: "ads.doubleclick.net", clientIp: "192.168.0.10", reason: "Ads" }
        ]);
    }
}

function renderLogs(logs: DnsLog[]) {
    logContainer.innerHTML = '';

    logs.forEach(log => {
        const div = document.createElement('div');
        div.className = `log-item ${log.reason.toLowerCase()}`;

        div.innerHTML = `
            <div>
                <strong>${log.domain}</strong>
                <div style="font-size: 0.8em; color: #888;">${new Date(log.timestamp).toLocaleTimeString()}</div>
            </div>
            <div>
                <span style="background: #444; padding: 4px 8px; border-radius: 4px; font-size: 0.8em;">${log.reason}</span>
            </div>
        `;

        div.onclick = () => showDetail(log);
        logContainer.appendChild(div);
    });
}

function showDetail(log: DnsLog) {
    selectedDomain = log.domain;
    detailDomain.innerText = log.domain;
    detailReason.innerText = log.reason;
    detailTime.innerText = new Date(log.timestamp).toLocaleString();
    detailIp.innerText = log.clientIp;

    detailPanel.style.display = 'block';
    detailPanel.scrollIntoView({ behavior: 'smooth' });
}

btnWhitelist.onclick = async () => {
    if (!selectedDomain) return;

    try {
        const response = await fetch(`${API_BASE}/whitelist`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ domain: selectedDomain })
        });

        if (response.ok) {
            alert(`O domínio ${selectedDomain} foi liberado e o cache DNS foi atualizado.`);
            detailPanel.style.display = 'none';
            fetchLogs();
        } else {
            alert("Falha ao comunicar com o Motor C#.");
        }
    } catch (error) {
        alert(`[SIMULAÇÃO] O comando POST foi enviado. O domínio "${selectedDomain}" seria liberado!`);
        detailPanel.style.display = 'none';
    }
};

fetchLogs();
setInterval(fetchLogs, 3000);