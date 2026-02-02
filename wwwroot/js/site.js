(function () {
    // --------------------------
    // Theme toggle (falls vorhanden)
    // --------------------------
    const themeToggle = document.getElementById("themeToggle");
    if (themeToggle) {
        themeToggle.addEventListener("click", () => {
            const root = document.documentElement;
            const current = root.getAttribute("data-theme") || "light";
            const next = current === "light" ? "dark" : "light";
            root.setAttribute("data-theme", next);
            try { localStorage.setItem("theme", next); } catch { }
        });

        try {
            const saved = localStorage.getItem("theme");
            if (saved) document.documentElement.setAttribute("data-theme", saved);
        } catch { }
    }

    // --------------------------
    // Sidebar open/close persistence (details)
    // --------------------------
    const DETAILS_KEY = "sidebar:details-open:v1";

    function loadOpenSet() {
        try {
            const raw = localStorage.getItem(DETAILS_KEY);
            if (!raw) return new Set();
            const arr = JSON.parse(raw);
            return new Set(Array.isArray(arr) ? arr : []);
        } catch {
            return new Set();
        }
    }

    function saveOpenSet(set) {
        try {
            localStorage.setItem(DETAILS_KEY, JSON.stringify(Array.from(set)));
        } catch { }
    }

    const openSet = loadOpenSet();

    document.querySelectorAll("details[data-node-key]").forEach(d => {
        const key = d.getAttribute("data-node-key");
        if (!key) return;

        // restore
        if (openSet.has(key)) d.open = true;

        // update on toggle
        d.addEventListener("toggle", () => {
            if (d.open) openSet.add(key);
            else openSet.delete(key);
            saveOpenSet(openSet);
        });
    });

    // --------------------------
    // Context menu + actions forms
    // --------------------------
    const ctxMenu = document.getElementById("contextMenu");
    const formCreateFolder = document.getElementById("ctxCreateFolderForm");
    const formCreateNotes = document.getElementById("ctxCreateNotesForm");
    const formDeleteNote = document.getElementById("ctxDeleteNoteForm");

    const ctxCustomerIdFolder = document.getElementById("ctxCustomerId_CreateFolder");
    const ctxFolderName = document.getElementById("ctxFolderName");
    const ctxParentRelFolder = document.getElementById("ctxParentRelPath_CreateFolder");

    const ctxCustomerIdNotes = document.getElementById("ctxCustomerId_CreateNotes");
    const ctxNotesTitle = document.getElementById("ctxNotesTitle");
    const ctxParentRelNotes = document.getElementById("ctxParentRelPath_CreateNotes");

    const ctxCustomerIdDeleteNote = document.getElementById("ctxCustomerId_DeleteNote");
    const ctxRelPathDeleteNote = document.getElementById("ctxRelPath_DeleteNote");

    let currentCtx = null;

    function hideCtx() {
        if (!ctxMenu) return;
        ctxMenu.hidden = true;
        currentCtx = null;
    }

    function showCtx(x, y, ctx) {
        if (!ctxMenu) return;
        currentCtx = ctx;

        ctxMenu.style.left = x + "px";
        ctxMenu.style.top = y + "px";
        ctxMenu.hidden = false;
    }

    document.addEventListener("click", () => hideCtx());
    document.addEventListener("keydown", (e) => { if (e.key === "Escape") hideCtx(); });

    // Attach contextmenu to folder + note + customer rows
    document.querySelectorAll("[data-node-type]").forEach(el => {
        el.addEventListener("contextmenu", (e) => {
            e.preventDefault();

            const nodeType = el.getAttribute("data-node-type");
            const customerId = el.getAttribute("data-customer-id");
            const relPath = el.getAttribute("data-relpath") || "";

            showCtx(e.pageX, e.pageY, { nodeType, customerId, relPath });
        });
    });

    if (ctxMenu) {
        ctxMenu.addEventListener("click", (e) => {
            const btn = e.target.closest("button[data-action]");
            if (!btn || !currentCtx) return;

            const action = btn.getAttribute("data-action");

            // Create Folder
            if (action === "create-folder") {
                const name = prompt("Folder Name:");
                if (!name) return hideCtx();

                if (!formCreateFolder) return hideCtx();
                ctxCustomerIdFolder.value = currentCtx.customerId;
                ctxFolderName.value = name;
                ctxParentRelFolder.value = (currentCtx.nodeType === "folder" ? currentCtx.relPath : "");
                formCreateFolder.submit();
                return;
            }

            // Create Notes
            if (action === "create-notes") {
                const title = prompt("Notes Titel (optional):") || "";
                if (!formCreateNotes) return hideCtx();

                ctxCustomerIdNotes.value = currentCtx.customerId;
                ctxNotesTitle.value = title;
                ctxParentRelNotes.value = (currentCtx.nodeType === "folder" ? currentCtx.relPath : "");
                formCreateNotes.submit();
                return;
            }

            // Delete Note (only on note nodes)
            if (action === "delete-note") {
                if (currentCtx.nodeType !== "note") return hideCtx();
                if (!confirm("Note wirklich löschen?")) return hideCtx();

                if (!formDeleteNote) return hideCtx();
                ctxCustomerIdDeleteNote.value = currentCtx.customerId;
                ctxRelPathDeleteNote.value = currentCtx.relPath;
                formDeleteNote.submit();
                return;
            }

            hideCtx();
        });
    }

    // --------------------------
    // Drag & Drop notes between folders
    // --------------------------
    const moveForm = document.getElementById("ctxMoveNoteForm");
    const moveCustomerId = document.getElementById("ctxCustomerId_MoveNote");
    const moveSourceRel = document.getElementById("ctxSourceRelPath_MoveNote");
    const moveTargetFolderRel = document.getElementById("ctxTargetFolderRelPath_MoveNote");

    let dragData = null;

    document.querySelectorAll(".draggable-note").forEach(a => {
        a.addEventListener("dragstart", (e) => {
            const customerId = a.getAttribute("data-customer-id");
            const relPath = a.getAttribute("data-relpath");
            dragData = { customerId, relPath };

            e.dataTransfer.effectAllowed = "move";
            try { e.dataTransfer.setData("text/plain", relPath); } catch { }
            a.classList.add("dragging");
        });

        a.addEventListener("dragend", () => {
            a.classList.remove("dragging");
            dragData = null;
            document.querySelectorAll(".drop-over").forEach(x => x.classList.remove("drop-over"));
        });
    });

    // Drop targets: folder summaries + customer summaries (root)
    document.querySelectorAll(".drop-target, .customer-row").forEach(t => {
        t.addEventListener("dragover", (e) => {
            if (!dragData) return;
            const cid = t.getAttribute("data-customer-id");
            if (cid !== dragData.customerId) return; // only within same customer
            e.preventDefault();
            t.classList.add("drop-over");
            e.dataTransfer.dropEffect = "move";
        });

        t.addEventListener("dragleave", () => t.classList.remove("drop-over"));

        t.addEventListener("drop", (e) => {
            if (!dragData || !moveForm) return;
            const cid = t.getAttribute("data-customer-id");
            if (cid !== dragData.customerId) return;

            e.preventDefault();
            t.classList.remove("drop-over");

            const nodeType = t.getAttribute("data-node-type");
            const targetRel = (nodeType === "folder") ? (t.getAttribute("data-relpath") || "") : "";

            moveCustomerId.value = dragData.customerId;
            moveSourceRel.value = dragData.relPath;
            moveTargetFolderRel.value = targetRel;

            moveForm.submit();
        });
    });

})();
