(function () {
    if (typeof Quill === "undefined") return;

    const editorEl = document.getElementById("editor");
    const toolbarEl = document.getElementById("editor-toolbar");
    const deltaEl = document.getElementById("DeltaJson");
    const formEl = document.getElementById("noteForm");

    if (!editorEl || !toolbarEl || !deltaEl || !formEl) return;

    const quill = new Quill(editorEl, {
        theme: "snow",
        modules: { toolbar: toolbarEl }
    });

    // Load delta
    try {
        const raw = (deltaEl.value || "").trim();
        if (raw.length > 0) {
            const delta = JSON.parse(raw);
            if (delta && Array.isArray(delta.ops)) {
                quill.setContents(delta);
            }
        }
    } catch {
        // ignore malformed delta; start empty
    }

    // Save delta on submit
    formEl.addEventListener("submit", function () {
        const delta = quill.getContents();
        deltaEl.value = JSON.stringify(delta);
    });
})();
