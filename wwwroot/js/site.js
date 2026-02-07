(function () {
    // --------------------------
    // Theme toggle
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

        if (openSet.has(key)) d.open = true;

        d.addEventListener("toggle", () => {
            if (d.open) openSet.add(key);
            else openSet.delete(key);
            saveOpenSet(openSet);
        });
    });

    // --------------------------
    // Context menus (3 separate)
    // --------------------------
    const menuFolder = document.getElementById("ctxMenuFolder");
    const menuNote = document.getElementById("ctxMenuNote");
    const menuDoc = document.getElementById("ctxMenuDoc");

    let currentCtx = null;

    function hideMenus() {
        [menuFolder, menuNote, menuDoc].forEach(m => { if (m) m.hidden = true; });
        currentCtx = null;
    }

    function showMenu(menu, x, y, ctx) {
        hideMenus();
        if (!menu) return;
        currentCtx = ctx;
        menu.style.left = x + "px";
        menu.style.top = y + "px";
        menu.hidden = false;
    }

    document.addEventListener("click", hideMenus);
    document.addEventListener("keydown", (e) => { if (e.key === "Escape") hideMenus(); });

    // --------------------------
    // Forms (existing + new)
    // --------------------------
    const formCreateFolder = document.getElementById("ctxCreateFolderForm");
    const formCreateNotes = document.getElementById("ctxCreateNotesForm");
    const formDeleteNote = document.getElementById("ctxDeleteNoteForm");
    const formMoveNote = document.getElementById("ctxMoveNoteForm");

    const formRenameNote = document.getElementById("ctxRenameNoteForm");
    const formRenameFolder = document.getElementById("ctxRenameFolderForm");
    const formDeleteFolder = document.getElementById("ctxDeleteFolderForm");

    const formRenameDoc = document.getElementById("ctxRenameDocumentForm");
    const formDeleteDoc = document.getElementById("ctxDeleteDocumentForm");
    const formMoveDoc = document.getElementById("ctxMoveDocumentForm");

    // NEW: Customer (DB) rename/delete forms
    const formRenameCustomer = document.getElementById("ctxRenameCustomerForm");
    const formDeleteCustomer = document.getElementById("ctxDeleteCustomerForm");

    // Inputs (existing)
    const ctxCustomerIdFolder = document.getElementById("ctxCustomerId_CreateFolder");
    const ctxFolderName = document.getElementById("ctxFolderName");
    const ctxParentRelFolder = document.getElementById("ctxParentRelPath_CreateFolder");

    const ctxCustomerIdNotes = document.getElementById("ctxCustomerId_CreateNotes");
    const ctxNotesTitle = document.getElementById("ctxNotesTitle");
    const ctxParentRelNotes = document.getElementById("ctxParentRelPath_CreateNotes");

    const ctxCustomerIdDeleteNote = document.getElementById("ctxCustomerId_DeleteNote");
    const ctxRelPathDeleteNote = document.getElementById("ctxRelPath_DeleteNote");

    const moveCustomerId = document.getElementById("ctxCustomerId_MoveNote");
    const moveSourceRel = document.getElementById("ctxSourceRelPath_MoveNote");
    const moveTargetFolderRel = document.getElementById("ctxTargetFolderRelPath_MoveNote");

    // Inputs (new)
    const ctxCustomerIdRenameNote = document.getElementById("ctxCustomerId_RenameNote");
    const ctxRelPathRenameNote = document.getElementById("ctxRelPath_RenameNote");
    const ctxNewNameRenameNote = document.getElementById("ctxNewName_RenameNote");

    const ctxCustomerIdRenameFolder = document.getElementById("ctxCustomerId_RenameFolder");
    const ctxRelPathRenameFolder = document.getElementById("ctxRelPath_RenameFolder");
    const ctxNewNameRenameFolder = document.getElementById("ctxNewName_RenameFolder");

    const ctxCustomerIdDeleteFolder = document.getElementById("ctxCustomerId_DeleteFolder");
    const ctxRelPathDeleteFolder = document.getElementById("ctxRelPath_DeleteFolder");

    const ctxDocIdRename = document.getElementById("ctxDocumentId_RenameDocument");
    const ctxDocNewName = document.getElementById("ctxNewName_RenameDocument");

    const ctxDocIdDelete = document.getElementById("ctxDocumentId_DeleteDocument");

    const ctxCustomerIdMoveDoc = document.getElementById("ctxCustomerId_MoveDocument");
    const ctxDocIdMoveDoc = document.getElementById("ctxDocumentId_MoveDocument");
    const ctxTargetFolderMoveDoc = document.getElementById("ctxTargetFolderId_MoveDocument");

    // NEW: Customer inputs
    const ctxCustomerIdRenameCustomer = document.getElementById("ctxCustomerId_RenameCustomer");
    const ctxNewNameRenameCustomer = document.getElementById("ctxNewName_RenameCustomer");
    const ctxCustomerIdDeleteCustomer = document.getElementById("ctxCustomerId_DeleteCustomer");

    // --------------------------
    // IMPORTANT FIX:
    // Use ONE global contextmenu handler + choose the correct target via closest()
    // (prevents docs/notes showing folder menu due to <details data-node-type="folder">)
    // --------------------------
    function getCtxTarget(startEl) {
        // Priority: doc > note > folder summary (drop-target) > customer row
        return (
            startEl.closest(".tree-doc") ||
            startEl.closest(".tree-note") ||
            startEl.closest(".drop-target") ||
            startEl.closest(".customer-row")
        );
    }

    document.addEventListener("contextmenu", (e) => {
        const target = getCtxTarget(e.target);
        if (!target) return;

        e.preventDefault();

        // Customer root: show folder menu but with relPath = "" (root)
        if (target.classList.contains("customer-row")) {
            const customerId = target.getAttribute("data-customer-id") || "";
            // Only if we actually have the ID; otherwise don't break anything
            if (!customerId) return;
            showMenu(menuFolder, e.pageX, e.pageY, { nodeType: "customer", customerId, relPath: "", docId: "" });
            return;
        }

        // Folder summary
        if (target.classList.contains("drop-target")) {
            const customerId = target.getAttribute("data-customer-id") || "";
            const relPath = target.getAttribute("data-relpath") || "";
            showMenu(menuFolder, e.pageX, e.pageY, { nodeType: "folder", customerId, relPath, docId: "" });
            return;
        }

        // Note
        if (target.classList.contains("tree-note")) {
            const customerId = target.getAttribute("data-customer-id") || "";
            const relPath = target.getAttribute("data-relpath") || "";
            showMenu(menuNote, e.pageX, e.pageY, { nodeType: "note", customerId, relPath, docId: "" });
            return;
        }

        // Doc
        if (target.classList.contains("tree-doc")) {
            const customerId = target.getAttribute("data-customer-id") || "";
            const docId = target.getAttribute("data-doc-id") || "";
            showMenu(menuDoc, e.pageX, e.pageY, { nodeType: "doc", customerId, relPath: "", docId });
            return;
        }
    }, true); // capture = true helps with nested elements

    // --------------------------
    // Menu click wiring
    // --------------------------
    function wireMenu(menu) {
        if (!menu) return;

        menu.addEventListener("click", (e) => {
            const btn = e.target.closest("button[data-action]");
            if (!btn || !currentCtx) return;

            const action = btn.getAttribute("data-action");

            // Folder actions (also for customer root create)
            if (action === "create-folder") {
                const name = prompt("Folder Name:");
                if (!name) return hideMenus();
                if (!formCreateFolder || !ctxCustomerIdFolder || !ctxFolderName || !ctxParentRelFolder) return hideMenus();

                ctxCustomerIdFolder.value = currentCtx.customerId;
                ctxFolderName.value = name;

                // If right-click on customer row => root
                ctxParentRelFolder.value = (currentCtx.nodeType === "folder") ? (currentCtx.relPath || "") : "";
                formCreateFolder.submit();
                return;
            }

            if (action === "create-notes") {
                const title = prompt("Notes Titel (optional):") || "";
                if (!formCreateNotes || !ctxCustomerIdNotes || !ctxNotesTitle || !ctxParentRelNotes) return hideMenus();

                ctxCustomerIdNotes.value = currentCtx.customerId;
                ctxNotesTitle.value = title;

                // If right-click on customer row => root
                ctxParentRelNotes.value = (currentCtx.nodeType === "folder") ? (currentCtx.relPath || "") : "";
                formCreateNotes.submit();
                return;
            }

            if (action === "rename-folder") {
                // NEW: Customer rename (DB)
                if (currentCtx.nodeType === "customer") {
                    const newName = prompt("Neuer Kundenname:");
                    if (!newName) return hideMenus();
                    if (!formRenameCustomer || !ctxCustomerIdRenameCustomer || !ctxNewNameRenameCustomer) return hideMenus();

                    ctxCustomerIdRenameCustomer.value = currentCtx.customerId;
                    ctxNewNameRenameCustomer.value = newName;
                    formRenameCustomer.submit();
                    return;
                }

                // Do not rename "customer root"
                if (currentCtx.nodeType !== "folder") return hideMenus();

                const newName = prompt("Neuer Ordnername:");
                if (!newName) return hideMenus();
                if (!formRenameFolder || !ctxCustomerIdRenameFolder || !ctxRelPathRenameFolder || !ctxNewNameRenameFolder) return hideMenus();

                ctxCustomerIdRenameFolder.value = currentCtx.customerId;
                ctxRelPathRenameFolder.value = currentCtx.relPath || "";
                ctxNewNameRenameFolder.value = newName;
                formRenameFolder.submit();
                return;
            }

            if (action === "delete-folder") {
                // NEW: Customer delete (DB)
                if (currentCtx.nodeType === "customer") {
                    if (!confirm("Kunde wirklich löschen? (Nur wenn leer)")) return hideMenus();
                    if (!formDeleteCustomer || !ctxCustomerIdDeleteCustomer) return hideMenus();

                    ctxCustomerIdDeleteCustomer.value = currentCtx.customerId;
                    formDeleteCustomer.submit();
                    return;
                }

                // Do not delete "customer root"
                if (currentCtx.nodeType !== "folder") return hideMenus();

                if (!confirm("Ordner inkl. Unterordner, Notes und Dokumente wirklich löschen?")) return hideMenus();
                if (!formDeleteFolder || !ctxCustomerIdDeleteFolder || !ctxRelPathDeleteFolder) return hideMenus();

                ctxCustomerIdDeleteFolder.value = currentCtx.customerId;
                ctxRelPathDeleteFolder.value = currentCtx.relPath || "";
                formDeleteFolder.submit();
                return;
            }

            // Note actions
            if (action === "delete-note") {
                if (!confirm("Note wirklich löschen?")) return hideMenus();
                if (!formDeleteNote || !ctxCustomerIdDeleteNote || !ctxRelPathDeleteNote) return hideMenus();

                ctxCustomerIdDeleteNote.value = currentCtx.customerId;
                ctxRelPathDeleteNote.value = currentCtx.relPath || "";
                formDeleteNote.submit();
                return;
            }

            if (action === "rename-note") {
                const newName = prompt("Neuer Notename (ohne Endung):");
                if (!newName) return hideMenus();
                if (!formRenameNote || !ctxCustomerIdRenameNote || !ctxRelPathRenameNote || !ctxNewNameRenameNote) return hideMenus();

                ctxCustomerIdRenameNote.value = currentCtx.customerId;
                ctxRelPathRenameNote.value = currentCtx.relPath || "";
                ctxNewNameRenameNote.value = newName;
                formRenameNote.submit();
                return;
            }

            // Doc actions
            if (action === "rename-document") {
                const newName = prompt("Neuer Dateiname:");
                if (!newName) return hideMenus();
                if (!formRenameDoc || !ctxDocIdRename || !ctxDocNewName) return hideMenus();

                ctxDocIdRename.value = currentCtx.docId || "";
                ctxDocNewName.value = newName;
                formRenameDoc.submit();
                return;
            }

            if (action === "delete-document") {
                if (!confirm("Dokument wirklich löschen?")) return hideMenus();
                if (!formDeleteDoc || !ctxDocIdDelete) return hideMenus();

                ctxDocIdDelete.value = currentCtx.docId || "";
                formDeleteDoc.submit();
                return;
            }

            hideMenus();
        });
    }

    wireMenu(menuFolder);
    wireMenu(menuNote);
    wireMenu(menuDoc);

    // --------------------------
    // Drag & Drop notes + docs between folders
    // --------------------------
    let dragData = null;

    // Notes draggable (existing behavior)
    document.querySelectorAll(".draggable-note").forEach(a => {
        a.addEventListener("dragstart", (e) => {
            const customerId = a.getAttribute("data-customer-id");
            const relPath = a.getAttribute("data-relpath");
            dragData = { kind: "note", customerId, relPath };

            e.dataTransfer.effectAllowed = "move";
            try { e.dataTransfer.setData("text/plain", relPath || ""); } catch { }
            a.classList.add("dragging");
        });

        a.addEventListener("dragend", () => {
            a.classList.remove("dragging");
            dragData = null;
            document.querySelectorAll(".drop-over").forEach(x => x.classList.remove("drop-over"));
        });
    });

    // Docs draggable
    document.querySelectorAll(".tree-doc").forEach(d => {
        d.setAttribute("draggable", "true");

        d.addEventListener("dragstart", (e) => {
            const customerId = d.getAttribute("data-customer-id");
            const docId = d.getAttribute("data-doc-id");
            dragData = { kind: "doc", customerId, docId };

            e.dataTransfer.effectAllowed = "move";
            try { e.dataTransfer.setData("text/plain", docId || ""); } catch { }
            d.classList.add("dragging");
        });

        d.addEventListener("dragend", () => {
            d.classList.remove("dragging");
            dragData = null;
            document.querySelectorAll(".drop-over").forEach(x => x.classList.remove("drop-over"));
        });
    });

    // Drop targets: folder summaries + customer rows (root)
    document.querySelectorAll(".drop-target, .customer-row").forEach(t => {
        t.addEventListener("dragover", (e) => {
            if (!dragData) return;
            const cid = t.getAttribute("data-customer-id");
            if (cid !== dragData.customerId) return;
            e.preventDefault();
            t.classList.add("drop-over");
            e.dataTransfer.dropEffect = "move";
        });

        t.addEventListener("dragleave", () => t.classList.remove("drop-over"));

        t.addEventListener("drop", (e) => {
            if (!dragData) return;
            const cid = t.getAttribute("data-customer-id");
            if (cid !== dragData.customerId) return;

            e.preventDefault();
            t.classList.remove("drop-over");

            const nodeType = t.getAttribute("data-node-type");
            const targetRel = (nodeType === "folder") ? (t.getAttribute("data-relpath") || "") : "";

            if (dragData.kind === "note") {
                if (!formMoveNote || !moveCustomerId || !moveSourceRel || !moveTargetFolderRel) return;

                moveCustomerId.value = dragData.customerId;
                moveSourceRel.value = dragData.relPath;
                moveTargetFolderRel.value = targetRel;

                formMoveNote.submit();
                return;
            }

            if (dragData.kind === "doc") {
                if (!formMoveDoc || !ctxCustomerIdMoveDoc || !ctxDocIdMoveDoc || !ctxTargetFolderMoveDoc) return;

                ctxCustomerIdMoveDoc.value = dragData.customerId;
                ctxDocIdMoveDoc.value = dragData.docId;
                ctxTargetFolderMoveDoc.value = targetRel || "root";

                formMoveDoc.submit();
                return;
            }
        });
    });

})();
