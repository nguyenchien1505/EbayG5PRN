// E02 request forms: server draft autosave, character counters and the two-step Return layout (E1).
import { createAutosave } from "./dispute-draft.mjs";

const form = document.querySelector("[data-dispute-form]");
if (form) {
    const status = form.querySelector("[data-draft-status]");
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value ?? "";
    const fields = ["OrderItemId", "Kind", "Reason", "Preference", "Details"];
    const messages = { saving: "Saving draft…", saved: "Draft saved", error: "Couldn't save draft — your text is still here." };

    const snapshot = () => {
        const data = new FormData(form), out = new URLSearchParams();
        for (const name of fields) { const value = data.get(name); if (value !== null) out.set(name, value); }
        return out.toString();
    };

    const autosave = createAutosave({
        save: async (body, { keepalive = false } = {}) => {
            const payload = new URLSearchParams(body);
            payload.set("__RequestVerificationToken", token);
            const response = await fetch(form.dataset.draftUrl, { method: "POST", body: payload, keepalive, credentials: "same-origin", headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error("Draft save failed: " + response.status);
        },
        onStatus: state => { if (status) status.textContent = messages[state] ?? ""; }
    });
    autosave.prime(snapshot());

    const changed = () => autosave.schedule(snapshot());
    form.addEventListener("input", changed);
    form.addEventListener("change", changed);
    // Save before leaving: tab hidden, navigation or close.
    document.addEventListener("visibilitychange", () => { if (document.visibilityState === "hidden") autosave.flush({ keepalive: true }); });
    window.addEventListener("pagehide", () => autosave.flush({ keepalive: true }));
    form.addEventListener("submit", () => autosave.stop());

    for (const box of form.querySelectorAll("[data-count-target]")) {
        const count = document.getElementById(box.dataset.countTarget);
        if (count) box.addEventListener("input", () => { count.textContent = box.value.length; });
    }

    if (form.hasAttribute("data-steps")) {
        const [step1, step2] = [1, 2].map(n => form.querySelector(`[data-step="${n}"]`));
        const next = form.querySelector("[data-next]"), back = form.querySelector("[data-back]");
        const confirm = form.querySelector("[data-confirm]"), details = form.querySelector("textarea[name=Details]");
        const remorse = form.querySelector("[data-remorse-note]");
        const chosen = () => form.querySelector('input[name="Reason"]:checked');
        const show = n => {
            step1.hidden = n !== 1; step2.hidden = n !== 2;
            (n === 1 ? (chosen() ?? form.querySelector('input[name="Reason"]')) : details)?.focus();
        };
        const sync = () => {
            if (remorse) remorse.hidden = chosen()?.dataset.changeOfMind !== "true";
            next.disabled = !chosen();
            confirm.disabled = details.value.trim().length === 0;
        };
        next.hidden = false; back.hidden = false;
        next.addEventListener("click", () => { if (chosen()) show(2); });
        back.addEventListener("click", () => show(1));
        form.addEventListener("input", sync);
        form.addEventListener("change", sync);
        sync();
        // Without JavaScript both steps stay visible; a restored reason resumes at step 2.
        step1.hidden = !!chosen(); step2.hidden = !chosen();
    }
}
