import test from "node:test";
import assert from "node:assert/strict";
import { ChatState, sequence, compareSequence, normalizeText } from "../src/EbayGroup5.Web/wwwroot/js/chat-state.mjs";
import { ChatRecoveryLoop, recoveryDelay, retryableSend } from "../src/EbayGroup5.Web/wwwroot/js/chat-recovery.mjs";

const dto = (id = 10) => ({ id, buyerId: 1, sellerId: 2, lastSequence: "0", ownReadSequence: "0", peerReadSequence: "0", unreadCount: "0", updatedAtUtc: "2026-10-06T00:00:00Z", peerName: "Seller" });
const message = (seq, fields = {}) => ({ id: Number(BigInt(seq) % 1000n), conversationId: 10, sequence: seq, clientMessageId: "a" + seq, senderId: 2, receiverId: 1, content: "Text " + seq, sentAtUtc: "2026-10-06T01:00:00Z", ...fields });
const state = () => { const s = new ChatState(1); s.upsert(dto()); s.select(10); return s; };

test("decimal cursors keep exact ordering above JS safe integers and reject malformed/overflow", () => {
    assert.equal(sequence("9223372036854775807"), 9223372036854775807n);
    assert.equal(compareSequence("9007199254740993", "9007199254740992"), 1);
    for (const bad of ["01", "+1", "-1", "1.0", " 1", "1e2", "9223372036854775808", 1]) assert.throws(() => sequence(bad));
});
test("event before history and duplicate ACK merge without duplicate row or unread increment", () => {
    const s = state(), m = message("1");
    s.merge(m, true); s.merge(m, true);
    s.page(10, { messages: [m], highWatermark: "1", hasMore: false });
    assert.equal(s.ordered(10).length, 1); assert.equal(s.conversations.get(10).unreadCount, "1");
    assert.equal(s.cache(10).cursor, "1");
});
test("unordered live/history/older pages remain chronological without rounding bigint", () => {
    const s = state();
    for (const seq of ["9007199254740994", "9007199254740992", "9007199254740993"]) s.merge(message(seq));
    s.page(10, { messages: [message("9007199254740991")], hasMore: true }, "before");
    assert.deepEqual(s.ordered(10).map(x => x.sequence), ["9007199254740991", "9007199254740992", "9007199254740993", "9007199254740994"]);
    assert.equal(s.cache(10).hasOlder, true);
});
test("stale inbox refresh cannot regress live preview, unread or current read cursor", () => {
    const s = state(); s.merge(message("2"), true); s.read({ conversationId: 10, readerId: 1, throughSequence: "2" });
    s.upsert({ ...dto(), lastSequence: "1", ownReadSequence: "0", unreadCount: "1", lastMessagePreview: "old" });
    assert.equal(s.conversations.get(10).lastSequence, "2"); assert.equal(s.conversations.get(10).ownReadSequence, "2");
    assert.equal(s.conversations.get(10).unreadCount, "0"); assert.equal(s.conversations.get(10).lastMessagePreview, "Text 2");
});
test("own and peer read events are separate and monotonic; unknown reader has no authority", () => {
    const s = state(); s.merge(message("3"), true);
    s.read({ conversationId: 10, readerId: 2, throughSequence: "3" });
    assert.equal(s.conversations.get(10).ownReadSequence, "0"); assert.equal(s.conversations.get(10).unreadCount, "1");
    s.read({ conversationId: 10, readerId: 99, throughSequence: "3" }); assert.equal(s.conversations.get(10).ownReadSequence, "0");
    s.read({ conversationId: 10, readerId: 1, throughSequence: "3" }); s.read({ conversationId: 10, readerId: 1, throughSequence: "1" });
    assert.equal(s.conversations.get(10).ownReadSequence, "3"); assert.equal(s.conversations.get(10).unreadCount, "0");
});
test("only selected, visible, focused, bottom, initialized and fully caught-up history may mark read", () => {
    const s = state(), viewing = { visible: true, focused: true, bottom: true, loading: false };
    s.merge(message("2"), true); assert.equal(s.readCandidate(10, viewing), null);
    s.page(10, { messages: [message("1")], highWatermark: "1", hasMore: false });
    assert.equal(s.readCandidate(10, viewing), null); // live2 arrived beyond snapshot1
    s.page(10, { messages: [message("2")], highWatermark: "2", newestCursor: "2" }, "after");
    assert.equal(s.readCandidate(10, viewing), "2");
    for (const field of ["visible", "focused", "bottom"]) assert.equal(s.readCandidate(10, { ...viewing, [field]: false }), null);
    assert.equal(s.readCandidate(10, { ...viewing, loading: true }), null);
    s.select(11); assert.equal(s.readCandidate(10, viewing), null);
});
test("retry retains immutable exact UUID/context/text and foreign sender UUID doesn't remove it", () => {
    const s = state(), command = { clientMessageId: "ABC", content: normalizeText("  first\r\nline\rLast  "), productId: 9 };
    const entry = s.beginPending(10, command, "2026-10-06T00:00:00Z"); entry.state = "failed";
    assert.throws(() => { entry.command.content = "changed"; });
    assert.equal(entry.command.content, "first\nline\nLast"); assert.equal(entry.command.clientMessageId, "ABC");
    s.merge(message("1", { clientMessageId: "abc", senderId: 2 })); assert.equal(s.pending.size, 1);
    s.merge(message("2", { clientMessageId: "abc", senderId: 1 })); assert.equal(s.pending.size, 0);
});
test("switching conversation invalidates async render tickets including switch-away-and-back", () => {
    const s = state(), ticket = s.generation; s.select(11); s.select(10);
    assert.equal(s.isSelected(10, ticket), false); assert.equal(s.isSelected(10, s.generation), true);
    s.merge(message("1", { conversationId: 11 })); assert.equal(s.ordered(10).length, 0); assert.equal(s.ordered(11).length, 1);
});

function clock() {
    let id = 0;
    const timers = new Map();
    return { timers, schedule: (callback, delay) => { timers.set(++id, { callback, delay }); return id; },
        cancel: key => timers.delete(key), fire: async () => { const [key, value] = timers.entries().next().value; timers.delete(key); await value.callback(); },
        delay: () => [...timers.values()][0]?.delay };
}
test("recovery retries initial start failures indefinitely with bounded jitter then returns to periodic reconciliation", async () => {
    const c = clock(); let attempts = 0, failures = 0;
    const loop = new ChatRecoveryLoop(async () => { if (++attempts <= 8) throw new Error("offline"); },
        { ...c, random: () => 1, failed: () => failures++ });
    loop.wake(); assert.equal(c.delay(), 0);
    for (const delay of [1000, 2000, 4000, 8000, 16000, 30000, 30000, 30000]) { await c.fire(); assert.equal(c.delay(), delay); }
    await c.fire(); assert.equal(c.delay(), 15000); assert.equal(failures, 8); assert.equal(loop.failures, 0);
    assert.equal(recoveryDelay(100, () => 0), 22500); loop.stop(); assert.equal(c.timers.size, 0);
});
test("online/focus/refresh wakeups during reconciliation coalesce and never overlap a start/rejoin cycle", async () => {
    const c = clock(); let release, concurrent = 0, max = 0;
    const loop = new ChatRecoveryLoop(async () => { max = Math.max(max, ++concurrent); await new Promise(resolve => { release = resolve; }); concurrent--; }, c);
    loop.wake(); const first = c.fire(); await Promise.resolve();
    for (let i = 0; i < 10; i++) loop.wake();
    assert.equal(c.timers.size, 0); assert.equal(max, 1);
    release(); await first; assert.equal(c.timers.size, 1); assert.equal(c.delay(), 0);
    loop.stop(); assert.equal(c.timers.size, 0);
});
test("hidden/closed/offline pages pause recovery; auth stop wins over an in-flight action", async () => {
    const c = clock(); let active = false, attempts = 0, release;
    const loop = new ChatRecoveryLoop(async () => { attempts++; await new Promise(resolve => { release = resolve; }); }, { ...c, active: () => active });
    loop.wake(); assert.equal(c.timers.size, 0);
    active = true; loop.wake(); active = false; await c.fire(); assert.equal(attempts, 0);
    active = true; loop.wake(); const running = c.fire(); await Promise.resolve();
    loop.stop(); release(); await running; loop.wake(); assert.equal(c.timers.size, 0); assert.equal(attempts, 1);
});
test("bounded catch-up continuation stays scheduled until the batch is complete", async () => {
    const c = clock(); let batches = 0;
    const loop = new ChatRecoveryLoop(async () => ++batches < 3 ? 250 : 15000, c);
    loop.wake(); await c.fire(); assert.equal(c.delay(), 250); await c.fire(); assert.equal(c.delay(), 250);
    await c.fire(); assert.equal(c.delay(), 15000); loop.stop();
});
test("transport failures auto-retry immutable pending; business denials stay manual", () => {
    for (const code of [0, 429, 500, 503]) assert.equal(retryableSend(code), true);
    for (const code of [400, 401, 403, 404, 409]) assert.equal(retryableSend(code), false);
    const s = state(); s.beginPending(10, { clientMessageId: "ABC", content: "exact payload" });
    s.merge(message("1", { clientMessageId: "abc", senderId: 1, conversationId: 11 })); assert.equal(s.pending.size, 1);
    s.page(10, { messages: [message("2", { clientMessageId: "abc", senderId: 1 })], highWatermark: "2", hasMore: false });
    assert.equal(s.pending.size, 0); assert.equal(s.ordered(10).length, 1);
});
test("a partial or failed catch-up cannot mark read even when the newest loaded row equals its cursor", () => {
    const s = state(), viewing = { visible: true, focused: true, bottom: true, loading: false };
    s.upsert({ ...dto(), lastSequence: "1100" });
    s.page(10, { messages: [message("1")], highWatermark: "1", hasMore: false });
    s.page(10, { messages: [message("1000")], newestCursor: "1000", highWatermark: "1100", hasMore: true }, "after");
    assert.equal(s.readCandidate(10, viewing), null);
    s.page(10, { messages: [message("1100")], newestCursor: "1100", highWatermark: "1100", hasMore: false }, "after");
    assert.equal(s.readCandidate(10, viewing), "1100");
    s.cache(10).caughtUp = false; assert.equal(s.readCandidate(10, viewing), null);
});
