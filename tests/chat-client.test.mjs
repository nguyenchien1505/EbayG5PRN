import test from "node:test";
import assert from "node:assert/strict";
import { ChatState, sequence, compareSequence, normalizeText } from "../src/EbayGroup5.Web/wwwroot/js/chat-state.mjs";

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
