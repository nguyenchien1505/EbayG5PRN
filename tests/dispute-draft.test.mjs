import test from "node:test";
import assert from "node:assert/strict";
import { createAutosave } from "../src/EbayGroup5.Web/wwwroot/js/dispute-draft.mjs";

// Manual timers so debounce is deterministic.
function harness({ fail = () => false } = {}) {
    const timers = new Map(); let nextId = 1;
    const saves = [], statuses = [], resolvers = [];
    const autosave = createAutosave({
        delay: 800,
        setTimer: (fn, ms) => { const id = nextId++; timers.set(id, { fn, ms }); return id; },
        clearTimer: id => timers.delete(id),
        onStatus: s => statuses.push(s),
        save: (snapshot, options) => new Promise((resolve, reject) => {
            saves.push({ snapshot, options });
            resolvers.push(() => (fail(snapshot) ? reject(new Error("boom")) : resolve()));
        })
    });
    const fire = () => { const all = [...timers.values()]; timers.clear(); all.forEach(t => t.fn()); };
    const settle = async () => { while (resolvers.length) { resolvers.shift()(); await new Promise(r => setImmediate(r)); } };
    return { autosave, timers, saves, statuses, fire, settle };
}

test("debounces typing into one save of the newest snapshot", async () => {
    const h = harness();
    h.autosave.prime("a=");
    h.autosave.schedule("a=1"); h.autosave.schedule("a=12"); h.autosave.schedule("a=123");
    assert.equal(h.timers.size, 1);
    assert.equal([...h.timers.values()][0].ms, 800);
    h.fire(); await h.settle();
    assert.deepEqual(h.saves.map(s => s.snapshot), ["a=123"]);
    assert.deepEqual(h.statuses, ["saving", "saved"]);
    assert.equal(h.autosave.pending, false);
});

test("an unchanged or primed snapshot is never sent", async () => {
    const h = harness();
    h.autosave.prime("restored");
    h.autosave.schedule("restored"); h.fire();
    await h.autosave.flush(); await h.settle();
    assert.equal(h.saves.length, 0);
});

test("flush saves immediately with options and cancels the pending timer", async () => {
    const h = harness();
    h.autosave.schedule("b=1");
    const done = h.autosave.flush({ keepalive: true });
    assert.equal(h.timers.size, 0);
    await h.settle(); await done;
    assert.deepEqual(h.saves, [{ snapshot: "b=1", options: { keepalive: true } }]);
});

test("saves run one at a time and the newest snapshot wins", async () => {
    const h = harness();
    h.autosave.schedule("v1"); const first = h.autosave.flush();
    h.autosave.schedule("v2"); const second = h.autosave.flush();
    assert.equal(h.saves.length, 1, "second save waits for the first");
    await h.settle(); await first; await second; await h.settle();
    assert.deepEqual(h.saves.map(s => s.snapshot), ["v1", "v2"]);
    assert.equal(h.statuses.at(-1), "saved");
});

test("a failed save reports an error and retries on the next change", async () => {
    let failing = true;
    const h = harness({ fail: () => failing });
    h.autosave.schedule("x"); h.fire(); await h.settle();
    assert.equal(h.statuses.at(-1), "error");
    assert.equal(h.autosave.pending, true);
    failing = false;
    h.autosave.schedule("xy"); h.fire(); await h.settle();
    assert.deepEqual(h.saves.map(s => s.snapshot), ["x", "xy"]);
    assert.equal(h.statuses.at(-1), "saved");
});

test("stop on submit prevents any later autosave", async () => {
    const h = harness();
    h.autosave.schedule("draft");
    h.autosave.stop();
    assert.equal(h.timers.size, 0);
    h.autosave.schedule("after submit"); h.fire();
    await h.autosave.flush(); await h.settle();
    assert.equal(h.saves.length, 0);
});
