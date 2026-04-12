let isRunning = false;

function formatDateTime(value) {
    if (!value) {
        return "-";
    }

    const date = new Date(value);
    return date.toLocaleString("zh-TW", { hour12: false });
}

function escapeHtml(value) {
    return String(value)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;");
}

function updateStateBadge(data) {
    const badge = document.getElementById("runningBadge");
    const statusText = document.getElementById("statusText");

    if (!badge || !statusText) {
        return;
    }

    if (data.isRunning) {
        badge.className = "sync-state-badge sync-state-running";
        badge.textContent = "執行中";
        statusText.textContent = "同步服務執行中";
        return;
    }

    if (data.lastRunSuccess === true) {
        badge.className = "sync-state-badge sync-state-success";
        badge.textContent = "成功";
        statusText.textContent = "最近一次同步成功";
        return;
    }

    if (data.lastRunSuccess === false) {
        badge.className = "sync-state-badge sync-state-failed";
        badge.textContent = "失敗";
        statusText.textContent = "最近一次同步失敗";
        return;
    }

    badge.className = "sync-state-badge sync-state-idle";
    badge.textContent = "尚未執行";
    statusText.textContent = "尚未有同步紀錄";
}

function updateActionState(data) {
    const button = document.getElementById("syncNowBtn");
    const spinner = document.getElementById("syncBtnSpinner");
    const buttonText = document.getElementById("syncBtnText");
    const intervalSelect = document.getElementById("intervalSelect");

    if (button && spinner && buttonText) {
        button.disabled = data.isRunning;
        spinner.classList.toggle("d-none", !data.isRunning);
        buttonText.textContent = data.isRunning ? "同步執行中" : "立即同步";
    }

    if (intervalSelect) {
        const option = intervalSelect.querySelector(`option[value="${data.intervalMinutes}"]`);
        if (option) {
            intervalSelect.value = data.intervalMinutes;
        }
    }
}

function updateLogs(logs) {
    const tbody = document.getElementById("logsTable");
    if (!tbody) {
        return;
    }

    if (!logs || logs.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="4" class="sync-empty-state">目前尚無同步記錄。</td>
            </tr>`;
        return;
    }

    tbody.innerHTML = logs.map((log) => `
        <tr>
            <td class="text-nowrap">${formatDateTime(log.time)}</td>
            <td>
                <span class="sync-log-result ${log.success ? "sync-log-result-success" : "sync-log-result-failed"}">
                    ${log.success ? "成功" : "失敗"}
                </span>
            </td>
            <td>${log.rowsSynced ?? 0}</td>
            <td>${escapeHtml(log.message ?? "-")}</td>
        </tr>`).join("");
}

function updateUI(data) {
    isRunning = data.isRunning;

    updateStateBadge(data);

    const lastRunTime = document.getElementById("lastRunTime");
    const lastRunMessage = document.getElementById("lastRunMessage");
    const lastRowsSynced = document.getElementById("lastRowsSynced");
    const nextRunTime = document.getElementById("nextRunTime");

    if (lastRunTime) {
        lastRunTime.textContent = formatDateTime(data.lastRunTime);
    }

    if (lastRunMessage) {
        lastRunMessage.textContent = data.lastRunMessage ?? "-";
    }

    if (lastRowsSynced) {
        lastRowsSynced.textContent = data.lastRowsSynced > 0 ? `${data.lastRowsSynced}` : "-";
    }

    if (nextRunTime) {
        nextRunTime.textContent = formatDateTime(data.nextRunTime);
    }

    updateActionState(data);
    updateLogs(data.recentLogs);
}

async function fetchStatus() {
    try {
        const response = await fetch("/api/sync/status");
        if (response.ok) {
            updateUI(await response.json());
        }
    } catch (_) {
        // Keep the current UI state when polling fails.
    }
}

function showIntervalMessage(message, isSuccess) {
    const element = document.getElementById("intervalMsg");
    if (!element) {
        return;
    }

    element.textContent = message;
    element.className = `sync-inline-message ${isSuccess ? "sync-inline-message-success" : "sync-inline-message-error"}`;
    element.classList.remove("d-none");

    setTimeout(() => {
        element.classList.add("d-none");
    }, 3000);
}

document.addEventListener("DOMContentLoaded", () => {
    const syncNowButton = document.getElementById("syncNowBtn");
    const setIntervalButton = document.getElementById("setIntervalBtn");
    const intervalSelect = document.getElementById("intervalSelect");

    syncNowButton?.addEventListener("click", async () => {
        if (isRunning) {
            return;
        }

        const response = await fetch("/api/sync/run", { method: "POST" });
        if (response.status === 409) {
            alert("同步已在執行中，請稍候再試。");
        }

        await fetchStatus();
    });

    setIntervalButton?.addEventListener("click", async () => {
        const minutes = Number(intervalSelect?.value ?? 30);
        const response = await fetch("/api/sync/interval", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ minutes })
        });

        if (response.ok) {
            const data = await response.json();
            showIntervalMessage(data.message, true);
        } else {
            showIntervalMessage("更新同步間隔失敗，請稍後再試。", false);
        }

        await fetchStatus();
    });

    fetchStatus();
    setInterval(fetchStatus, 3000);
});
