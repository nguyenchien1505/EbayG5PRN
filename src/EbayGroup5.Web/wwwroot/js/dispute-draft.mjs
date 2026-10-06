// E02 draft autosave (DISPUTE_FORMS.md): debounce, flush on demand, one save in flight, never resend an unchanged snapshot.
// Pure and timer-injectable so tests/dispute-draft.test.mjs can drive it without a browser.
export function createAutosave({ save, delay = 800, setTimer = setTimeout, clearTimer = clearTimeout, onStatus = () => {} }) {
    let timer = null;
    let saved = null;     // last snapshot the server accepted
    let latest = null;    // newest snapshot seen
    let inFlight = null;  // promise of the save currently running
    let stopped = false;

    function cancelTimer() {
        if (timer !== null) { clearTimer(timer); timer = null; }
    }

    function schedule(snapshot) {
        if (stopped) return;
        latest = snapshot;
        cancelTimer();
        timer = setTimer(() => { timer = null; flush(); }, delay);
    }

    function flush(options = {}) {
        cancelTimer();
        if (stopped || latest === null || latest === saved) return inFlight ?? Promise.resolve();
        // Saves run strictly one after another so an older snapshot can never overwrite a newer one.
        if (inFlight) return inFlight.then(() => flush(options));
        const snapshot = latest;
        onStatus("saving");
        inFlight = (async () => {
            let ok = false;
            try {
                await save(snapshot, options);
                saved = snapshot;
                ok = true;
            } catch {
                onStatus("error");
            } finally {
                inFlight = null;
            }
            if (!ok) return;
            if (latest !== saved && !stopped) await flush(options);
            else onStatus("saved");
        })();
        return inFlight;
    }

    // Marks server-rendered state (restored draft) as already saved.
    function prime(snapshot) { saved = latest = snapshot; }

    // Called on submit: no later autosave may recreate the draft the server deletes.
    function stop() { stopped = true; cancelTimer(); }

    return { schedule, flush, prime, stop, get pending() { return latest !== saved; } };
}
