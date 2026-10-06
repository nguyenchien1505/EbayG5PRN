const maxSequence = 9223372036854775807n;
export function sequence(value) {
    if (typeof value !== "string" || !/^(0|[1-9][0-9]*)$/.test(value)) throw new Error("Invalid cursor.");
    const parsed = BigInt(value);
    if (parsed > maxSequence) throw new Error("Invalid cursor.");
    return parsed;
}
export const compareSequence = (a, b) => sequence(a) < sequence(b) ? -1 : sequence(a) > sequence(b) ? 1 : 0;
const highest = (a, b) => compareSequence(a, b) >= 0 ? a : b;
export const normalizeText = text => text.replace(/\r\n?/g, "\n").trim();

export class ChatState {
    constructor(actorId) {
        this.actorId = actorId;
        this.conversations = new Map();
        this.caches = new Map();
        this.pending = new Map();
        this.selectedId = null;
        this.generation = 0;
    }
    cache(id) {
        if (!this.caches.has(id)) this.caches.set(id, { messages: new Map(), cursor: "0", initialized: false, caughtUp: false, hasOlder: false });
        return this.caches.get(id);
    }
    select(id) { this.selectedId = id; return ++this.generation; }
    isSelected(id, generation) { return this.selectedId === id && this.generation === generation; }
    upsert(dto) {
        const old = this.conversations.get(dto.id);
        if (!old) { this.conversations.set(dto.id, { ...dto }); return; }
        const stale = compareSequence(dto.lastSequence, old.lastSequence) < 0 || compareSequence(dto.ownReadSequence, old.ownReadSequence) < 0;
        this.conversations.set(dto.id, { ...dto,
            lastSequence: highest(old.lastSequence, dto.lastSequence),
            ownReadSequence: highest(old.ownReadSequence, dto.ownReadSequence),
            peerReadSequence: highest(old.peerReadSequence, dto.peerReadSequence),
            unreadCount: stale ? old.unreadCount : dto.unreadCount,
            updatedAtUtc: stale ? old.updatedAtUtc : dto.updatedAtUtc,
            lastMessagePreview: stale ? old.lastMessagePreview : dto.lastMessagePreview,
            lastMessageAtUtc: stale ? old.lastMessageAtUtc : dto.lastMessageAtUtc });
    }
    merge(message, live = false) {
        const cache = this.cache(message.conversationId);
        const existing = cache.messages.get(message.sequence);
        cache.messages.set(message.sequence, message);
        const pending = this.pending.get(message.clientMessageId.toLowerCase());
        if (message.senderId === this.actorId && pending?.conversationId === message.conversationId) this.pending.delete(message.clientMessageId.toLowerCase());
        const conversation = this.conversations.get(message.conversationId);
        if (live && conversation && compareSequence(message.sequence, conversation.lastSequence) > 0) {
            conversation.lastSequence = message.sequence;
            conversation.lastMessagePreview = message.content.slice(0, 120);
            conversation.lastMessageAtUtc = conversation.updatedAtUtc = message.sentAtUtc;
            if (message.senderId !== this.actorId && compareSequence(message.sequence, conversation.ownReadSequence) > 0)
                conversation.unreadCount = (sequence(conversation.unreadCount) + 1n).toString();
        }
        return !existing;
    }
    page(id, page, kind = "latest") {
        for (const message of page.messages) this.merge(message);
        const cache = this.cache(id);
        if (kind === "latest") {
            cache.initialized = true;
            cache.cursor = page.highWatermark;
            cache.caughtUp = true;
            cache.hasOlder = page.hasMore;
        } else if (kind === "after") {
            cache.cursor = highest(cache.cursor, page.newestCursor ?? page.highWatermark);
            cache.caughtUp = !page.hasMore && compareSequence(cache.cursor, page.highWatermark) >= 0;
        } else cache.hasOlder = page.hasMore;
    }
    ordered(id) { return [...this.cache(id).messages.values()].sort((a, b) => compareSequence(a.sequence, b.sequence)); }
    beginPending(id, command, clientTime) {
        const key = command.clientMessageId.toLowerCase();
        const entry = { conversationId: id, command: Object.freeze({ ...command }), state: "sending", clientTime };
        this.pending.set(key, entry);
        return entry;
    }
    read(receipt) {
        const dto = this.conversations.get(receipt.conversationId);
        if (!dto) return;
        const own = receipt.readerId === this.actorId;
        const peer = this.actorId === dto.buyerId ? dto.sellerId : dto.buyerId;
        if (!own && receipt.readerId !== peer) return;
        const key = own ? "ownReadSequence" : "peerReadSequence";
        dto[key] = highest(dto[key], receipt.throughSequence);
        if (own && compareSequence(dto.ownReadSequence, dto.lastSequence) >= 0) dto.unreadCount = "0";
    }
    readCandidate(id, { visible, focused, bottom, loading }) {
        const cache = this.cache(id), dto = this.conversations.get(id), messages = this.ordered(id);
        const newest = messages.at(-1)?.sequence;
        if (id !== this.selectedId || !visible || !focused || !bottom || loading || !cache.initialized || !cache.caughtUp || !newest || !dto ||
            compareSequence(cache.cursor, dto.lastSequence) < 0 ||
            compareSequence(newest, cache.cursor) > 0 || compareSequence(newest, dto.ownReadSequence) <= 0) return null;
        return newest;
    }
}
