
document.addEventListener("DOMContentLoaded", () => {
    initializeNoticeForm();

    const scheduleStateElement = document.getElementById("oneAtimeScheduleState");
    if (!scheduleStateElement) {
        return;
    }

    const hiddenInputsContainer = document.getElementById("scheduleHiddenInputs");
    const scheduleForm = document.getElementById("scheduleSaveForm");
    const modalElement = document.getElementById("scheduleEditModal");
    const modalTitle = document.getElementById("scheduleEditModalTitle");
    const testItemInput = document.getElementById("modalTestItem");
    const progressInput = document.getElementById("modalProgress");
    const deleteButton = document.getElementById("deleteScheduleBtn");
    const saveButton = document.getElementById("saveScheduleBtn");

    if (!hiddenInputsContainer || !scheduleForm || !modalElement || !modalTitle || !testItemInput || !progressInput || !deleteButton || !saveButton) {
        return;
    }

    const scheduleState = JSON.parse(scheduleStateElement.textContent || "[]");
    const scheduleModal = new bootstrap.Modal(modalElement);

    let currentDateKey = null;
    let currentEntryIndex = null;
    let currentMode = "edit";

    document.querySelectorAll("[data-mode='edit'], [data-mode='add']").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.preventDefault();
            openEditor(button.dataset.date, button.dataset.mode, button.dataset.entryIndex);
        });
    });

    saveButton.addEventListener("click", () => {
        const day = findDay(currentDateKey);
        if (!day) {
            return;
        }

        const testItem = (testItemInput.value || "").trim();
        if (!testItem) {
            testItemInput.focus();
            return;
        }

        const nextEntry = {
            displayOrder: currentMode === "add" ? day.entries.length + 1 : currentEntryIndex + 1,
            testItem,
            progress: clampProgress(progressInput.value)
        };

        if (currentMode === "add") {
            day.entries.push(nextEntry);
        } else if (currentEntryIndex !== null && day.entries[currentEntryIndex]) {
            day.entries[currentEntryIndex] = nextEntry;
        }

        normalizeEntries(day);
        submitScheduleForm();
    });

    deleteButton.addEventListener("click", () => {
        const day = findDay(currentDateKey);
        if (!day || currentEntryIndex === null || currentMode === "add") {
            return;
        }

        day.entries.splice(currentEntryIndex, 1);
        normalizeEntries(day);
        submitScheduleForm();
    });

    function openEditor(dateKey, mode, entryIndexValue) {
        const day = findDay(dateKey);
        if (!day) {
            return;
        }

        currentDateKey = dateKey;
        currentMode = mode;
        currentEntryIndex = mode === "edit" ? Number(entryIndexValue) : null;

        if (mode === "edit" && currentEntryIndex !== null && day.entries[currentEntryIndex]) {
            const entry = day.entries[currentEntryIndex];
            modalTitle.textContent = `${day.dateLabel} - 修改機台資料`;
            testItemInput.value = entry.testItem ?? "";
            progressInput.value = clampProgress(entry.progress);
            deleteButton.classList.remove("d-none");
        } else {
            modalTitle.textContent = `${day.dateLabel} - 新增機台資料`;
            testItemInput.value = "";
            progressInput.value = 0;
            deleteButton.classList.add("d-none");
        }

        scheduleModal.show();
        setTimeout(() => testItemInput.focus(), 150);
    }

    function submitScheduleForm() {
        syncHiddenInputs();
        scheduleModal.hide();
        scheduleForm.submit();
    }

    function syncHiddenInputs() {
        hiddenInputsContainer.innerHTML = "";

        scheduleState.forEach((day, dayIndex) => {
            appendHiddenInput(`Days[${dayIndex}].ScheduleDate`, day.date);

            day.entries.forEach((entry, entryIndex) => {
                appendHiddenInput(`Days[${dayIndex}].Entries[${entryIndex}].DisplayOrder`, entryIndex + 1);
                appendHiddenInput(`Days[${dayIndex}].Entries[${entryIndex}].TestItem`, entry.testItem ?? "");
                appendHiddenInput(`Days[${dayIndex}].Entries[${entryIndex}].ScheduledTime`, "");
                appendHiddenInput(`Days[${dayIndex}].Entries[${entryIndex}].Progress`, clampProgress(entry.progress));
            });
        });
    }

    function appendHiddenInput(name, value) {
        const input = document.createElement("input");
        input.type = "hidden";
        input.name = name;
        input.value = value;
        hiddenInputsContainer.appendChild(input);
    }

    function findDay(dateKey) {
        return scheduleState.find((day) => day.date === dateKey);
    }

    function normalizeEntries(day) {
        day.entries = day.entries.map((entry, index) => ({
            displayOrder: index + 1,
            testItem: entry.testItem,
            progress: clampProgress(entry.progress)
        }));
    }

    function clampProgress(value) {
        const number = Number(value);

        if (Number.isNaN(number)) {
            return 0;
        }

        return Math.max(0, Math.min(100, Math.round(number)));
    }
});

function initializeNoticeForm() {
    const showNoticeFormBtn = document.getElementById("showNoticeFormBtn");
    const cancelNoticeFormBtn = document.getElementById("cancelNoticeFormBtn");
    const noticeAddForm = document.getElementById("noticeAddForm");
    const noticeText = document.getElementById("noticeText");

    if (!showNoticeFormBtn || !cancelNoticeFormBtn || !noticeAddForm) {
        return;
    }

    showNoticeFormBtn.addEventListener("click", () => {
        noticeAddForm.classList.remove("d-none");
        showNoticeFormBtn.classList.add("d-none");
        noticeText?.focus();
    });

    cancelNoticeFormBtn.addEventListener("click", () => {
        noticeAddForm.classList.add("d-none");
        showNoticeFormBtn.classList.remove("d-none");

        if (noticeText) {
            noticeText.value = "";
        }
    });
}
