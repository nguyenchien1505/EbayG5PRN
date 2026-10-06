import { ChatState, compareSequence, normalizeText } from "./chat-state.mjs";
import { ChatRecoveryLoop, retryableSend } from "./chat-recovery.mjs";

const root = document.querySelector("[data-chat-root]");
if (root) {
    const state = new ChatState(Number(root.dataset.actorId));
    const panel = root.closest("dialog"), inboxMode = root.dataset.mode === "inbox";
    const get = name => root.querySelector(`[data-${name}]`);
    const transcript = get("transcript"), input = get("input"), send = get("send");
    let connection, csrf, ready = false, sending = false, loadingHistory = false, nextInbox = null;
    let inboxBusy = false, inboxLoaded = false, sendContext = null, readBusy = false, readTimer, lastTrigger;
    const subscriptions = new Set(), drafts = new Map(), catchups = new Map();
    let startTask, subscriptionTask = Promise.resolve(), sessionEnded = false;
    const recovery = new ChatRecoveryLoop(reconcile, {
        active: () => !sessionEnded && isOpen() && document.visibilityState === "visible" && navigator.onLine,
        failed: error => {
            if (sessionEnded) return;
            if (error.liveUnavailable) status(error.more ? "Live updates paused. Catching up from saved messages…" : "Saved messages are up to date. Retrying live updates automatically…");
            else if (error.pendingConfirmation) status("Message confirmation is pending. Retrying automatically…");
            else showFailure(error);
        }
    });
    const timeFormat = new Intl.DateTimeFormat("en-US", { hour: "numeric", minute: "2-digit" });
    const dateFormat = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric" });
    const isOpen = () => inboxMode || panel.open;
    const isBottom = () => transcript.scrollHeight - transcript.scrollTop - transcript.clientHeight < 64;
    const element = (tag, className, text) => { const node = document.createElement(tag); if (className) node.className = className; if (text != null) node.textContent = text; return node; };
    const status = (message, error = false) => { const node = get("status"); node.textContent = message; node.hidden = !message; node.classList.toggle("error", error); if (!message) get("retry-load").hidden = true; };
    const errorCode = error => error?.status ?? Number(error?.message?.match(/HubException: (400|401|403|404|409|500):/)?.[1] ?? 0);
    function signIn() {
        sessionEnded = true; recovery.stop(); csrf = null; drafts.clear(); subscriptions.clear();
        get("connection").textContent = "Signed out";
        connection?.stop().catch(() => {});
        ready = false; state.conversations.clear(); state.caches.clear(); state.pending.clear(); state.select(null);
        get("message-list").replaceChildren(); get("conversations")?.replaceChildren(); input.value = ""; input.disabled = true;
        status("Your session has ended. Sign in to continue.", true);
        const link = element("a", "", "Sign in"); link.href = "/Account/Login?returnUrl=" + encodeURIComponent(location.pathname + location.search); get("status").append(document.createTextNode(" "), link);
        updateComposer();
    }
    function showFailure(error, action = "load messages") {
        const code = errorCode(error);
        if (code === 401) { signIn(); return; }
        status(code === 404 || code === 403 ? "This conversation is unavailable." : code === 409
            ? "This conversation is currently unavailable. Please try again later."
            : `Couldn't ${action}. Please try again.`, true);
        if (action === "load messages" && code !== 404 && code !== 403) get("retry-load").hidden = false;
    }
    async function request(path, body) {
        const response = await fetch("/api/v1/chat/" + path, {
            credentials: "same-origin", cache: "no-store", method: body === undefined ? "GET" : "POST",
            signal: AbortSignal.timeout(15000),
            headers: body === undefined ? {} : { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf },
            body: body === undefined ? undefined : JSON.stringify(body)
        });
        if (!response.ok) { const error = new Error("Chat request failed."); error.status = response.status; throw error; }
        return response.json();
    }
    async function token() { csrf = (await request("antiforgery")).requestToken; }
    function updateComposer() {
        const text = normalizeText(input.value);
        get("count").textContent = `${input.value.length} / 4000`;
        input.disabled = !ready || !state.selectedId;
        send.disabled = input.disabled || sending || !text || text.length > 4000;
    }
    function header() {
        const dto = state.conversations.get(state.selectedId);
        get("peer-name").textContent = dto?.peerName || "Select a conversation";
        get("peer-avatar").textContent = (dto?.peerName || "M").slice(0, 1).toUpperCase();
        const context = sendContext || dto?.initialContext;
        get("context").hidden = !context;
        if (context) {
            get("item-link").textContent = context.title; get("item-link").href = "/Catalog/Details/" + context.productId;
            get("item-price").textContent = (context.orderItemId ? "Purchase price: " : "US ") + "$" + Number(context.unitPrice).toFixed(2);
        }
    }
    function renderInbox() {
        if (!inboxMode) return;
        const list = get("conversations"); list.replaceChildren();
        const sorted = [...state.conversations.values()].sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc) || b.id - a.id);
        for (const dto of sorted) {
            const row = element("a", "chat-row" + (dto.id === state.selectedId ? " selected" : ""));
            row.href = "/Messages?conversationId=" + dto.id; row.dataset.conversationId = String(dto.id);
            if (dto.id === state.selectedId) row.setAttribute("aria-current", "true");
            row.append(element("span", "chat-avatar", (dto.peerName || "M").slice(0, 1).toUpperCase()));
            const text = element("div"), top = element("div", "chat-row-top");
            top.append(element("strong", "chat-row-name", dto.peerName), element("span", "chat-row-date", dto.lastMessageAtUtc ? dateFormat.format(new Date(dto.lastMessageAtUtc)) : ""));
            const failed = [...state.pending.values()].find(x => x.conversationId === dto.id && x.state === "failed");
            text.append(top, element("span", "chat-row-item", dto.initialContext?.title || "Conversation"),
                element("span", "chat-row-preview", failed ? "Not sent · " + failed.command.content : dto.lastMessagePreview || "Start the conversation"));
            row.append(text);
            if (dto.unreadCount !== "0") { const badge = element("span", "chat-unread", dto.unreadCount); badge.setAttribute("aria-label", dto.unreadCount + " unread messages"); row.append(badge); }
            row.addEventListener("click", event => { event.preventDefault(); selectConversation(dto.id).catch(showFailure); });
            list.append(row);
        }
        get("inbox-notice").textContent = sorted.length ? "" : "No messages yet. Conversations with buyers and sellers will appear here.";
        get("more-conversations").hidden = !nextInbox;
    }
    function messageNode(message, pending = null) {
        const own = pending || message.senderId === state.actorId;
        const wrapper = element("article", "chat-message" + (own ? " own" : "") + (pending?.state === "failed" ? " failed" : ""));
        if (message.sequence) wrapper.dataset.sequence = message.sequence;
        if (pending) wrapper.dataset.clientMessageId = pending.command.clientMessageId;
        wrapper.append(element("div", "chat-bubble", pending ? pending.command.content : message.content));
        const meta = element("div", "chat-message-meta"), time = pending?.clientTime || message.sentAtUtc;
        meta.append(element("time", "", timeFormat.format(new Date(time))));
        if (pending) {
            meta.append(element("span", "", pending.state === "failed" ? "Not sent" : "Sending…"));
            if (pending.state === "failed") {
                const retry = element("button", "chat-retry", "Retry"); retry.type = "button";
                retry.disabled = sending; retry.addEventListener("click", () => submitPending(pending)); meta.append(retry);
            }
        } else if (own) {
            const peerRead = state.conversations.get(message.conversationId)?.peerReadSequence || "0";
            meta.append(element("span", "", compareSequence(message.sequence, peerRead) <= 0 ? "Read" : "Sent"));
        }
        wrapper.append(meta); return wrapper;
    }
    function renderMessages(scroll = "keep") {
        const id = state.selectedId; if (!id) return;
        const oldTop = transcript.scrollTop, oldHeight = transcript.scrollHeight;
        const list = get("message-list"), messages = state.ordered(id); list.replaceChildren();
        let day = "";
        for (const message of messages) {
            const nextDay = dateFormat.format(new Date(message.sentAtUtc));
            if (nextDay !== day) { list.append(element("div", "chat-day", nextDay)); day = nextDay; }
            list.append(messageNode(message));
        }
        const pending = [...state.pending.values()].filter(x => x.conversationId === id);
        for (const entry of pending) list.append(messageNode({}, entry));
        get("thread-empty").hidden = !!(messages.length || pending.length);
        get("thread-empty").textContent = loadingHistory ? "Loading messages…" : "No messages yet. Say hello.";
        get("more-messages").hidden = !state.cache(id).hasOlder;
        if (scroll === "bottom") transcript.scrollTop = transcript.scrollHeight;
        else if (scroll === "older") transcript.scrollTop = oldTop + transcript.scrollHeight - oldHeight;
        else transcript.scrollTop = oldTop;
    }
    async function ensureConnection() {
        if (!connection) {
            if (!window.signalR) throw new Error("Chat client unavailable.");
            connection = new signalR.HubConnectionBuilder().withUrl("/hubs/chat", { transport: signalR.HttpTransportType.WebSockets, skipNegotiation: true })
                .configureLogging(signalR.LogLevel.None).build();
            connection.on("MessageCommitted", message => {
                if (sessionEnded) return;
                const bottom = isBottom(), added = state.merge(message, true);
                renderInbox();
                if (message.conversationId === state.selectedId && isOpen()) {
                    renderMessages(bottom ? "bottom" : "keep");
                    if (added && !bottom) get("new-messages").hidden = false;
                    catchup().then(more => { if (more) recovery.wake(); }).catch(error => { showFailure(error); recovery.wake(); });
                }
            });
            connection.on("ReadAdvanced", receipt => { if (sessionEnded) return; state.read(receipt); renderInbox(); if (receipt.conversationId === state.selectedId) renderMessages(); });
            connection.onclose(() => { subscriptions.clear(); if (sessionEnded) return; get("connection").textContent = "Reconnecting…";
                if (isOpen() && !sessionEnded) { status("Connection lost. Reconnecting automatically…"); recovery.wake(); } });
        }
        if (connection.state === signalR.HubConnectionState.Disconnected) {
            if (!startTask) {
                get("connection").textContent = "Connecting…";
                startTask = connection.start().then(() => { subscriptions.clear(); if (!sessionEnded) get("connection").textContent = "Connected"; })
                    .finally(() => { startTask = null; });
            }
            await startTask;
        }
        else if (startTask) await startTask;
    }
    function syncSubscriptions(force = false) {
        const task = subscriptionTask.catch(() => {}).then(() => updateSubscriptions(force));
        subscriptionTask = task; return task;
    }
    async function updateSubscriptions(force) {
        if (connection?.state !== signalR.HubConnectionState.Connected) return;
        const desired = new Set(inboxMode ? [...state.conversations.values()]
            .sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc) || b.id - a.id).slice(0, 20).map(x => x.id) : []);
        if (state.selectedId && isOpen()) desired.add(state.selectedId);
        for (const id of subscriptions) if (!desired.has(id)) { await connection.invoke("LeaveConversation", id); subscriptions.delete(id); }
        for (const id of desired) if (force || !subscriptions.has(id)) {
            try {
                const dto = await connection.invoke("JoinConversation", id);
                if (sessionEnded) return;
                state.upsert(dto); subscriptions.add(id);
            } catch (error) {
                if (errorCode(error) !== 404) throw error;
                state.conversations.delete(id); subscriptions.delete(id);
                if (id === state.selectedId) { ready = false; showFailure(error); updateComposer(); }
            }
        }
        renderInbox();
    }
    async function loadInbox(more = false, propagate = false) {
        if (!inboxMode || inboxBusy) return;
        inboxBusy = true; get("more-conversations").disabled = true;
        try {
            const cursor = more ? nextInbox : null;
            const query = cursor ? "?cursorUtc=" + encodeURIComponent(cursor.updatedAtUtc) + "&cursorId=" + cursor.conversationId : "";
            const page = await request("conversations" + query);
            if (sessionEnded) return;
            for (const dto of page.conversations) state.upsert(dto);
            if (more || !inboxLoaded) nextInbox = page.nextCursor;
            inboxLoaded = true; renderInbox(); await syncSubscriptions();
        } catch (error) {
            if (errorCode(error) === 401) signIn();
            else get("inbox-notice").textContent = "Couldn't load conversations. Choose Refresh messages to try again.";
            if (propagate) throw error;
        } finally { inboxBusy = false; get("more-conversations").disabled = false; }
    }
    async function selectConversation(id, dto = null, context = null) {
        if (state.selectedId) drafts.set(state.selectedId, input.value);
        const ticket = state.select(id); ready = false; loadingHistory = true; sendContext = context;
        if (dto) state.upsert(dto);
        if (inboxMode) { root.classList.add("thread-open"); history.replaceState(null, "", "/Messages?conversationId=" + id); }
        get("more-messages").disabled = true;
        input.value = drafts.get(id) || ""; updateComposer(); header(); renderInbox(); renderMessages("bottom"); status("");
        let connected = true;
        try { await ensureConnection(); await syncSubscriptions(); }
        catch { connected = false; get("connection").textContent = "Live updates paused"; }
        try {
            // Returning to a cached thread must preserve its contiguous cursor, even after a long outage.
            const cached = state.cache(id).initialized;
            const page = await request(`conversations/${id}/messages` + (cached ? `?after=${state.cache(id).cursor}&limit=100` : ""));
            if (!state.isSelected(id, ticket) || !isOpen()) return;
            state.page(id, page, cached ? "after" : "latest"); loadingHistory = false; ready = true; get("more-messages").disabled = false;
            renderMessages("bottom"); updateComposer();
            if (!connected) status("Live updates are unavailable. You can send messages or refresh the conversation.");
            await catchup(); scheduleRead(); if (!recovery.running) recovery.wake();
        } catch (error) { if (state.isSelected(id, ticket)) { loadingHistory = false; showFailure(error); updateComposer(); if (!recovery.running) recovery.wake(); } }
    }
    async function catchup() {
        const id = state.selectedId, ticket = state.generation;
        if (!id || !state.cache(id).initialized || loadingHistory) return;
        if (catchups.has(id)) return catchups.get(id);
        const task = (async () => { try {
            state.cache(id).caughtUp = false;
            let more = true, pages = 0;
            while (more && pages++ < 10 && state.isSelected(id, ticket) && isOpen()) {
                const cache = state.cache(id), page = await request(`conversations/${id}/messages?after=${cache.cursor}&limit=100`);
                if (!state.isSelected(id, ticket) || !isOpen()) return;
                const bottom = isBottom(); state.page(id, page, "after");
                const newest = state.ordered(id).at(-1)?.sequence;
                more = page.hasMore || !!(newest && compareSequence(newest, state.cache(id).cursor) > 0);
                renderMessages(bottom ? "bottom" : "keep");
            }
            return more && state.isSelected(id, ticket) && isOpen();
        } finally { catchups.delete(id); scheduleRead(); } })();
        catchups.set(id, task);
        return task;
    }
    async function olderMessages() {
        const id = state.selectedId, ticket = state.generation, oldest = state.ordered(id)[0]?.sequence;
        if (!id || !oldest || loadingHistory) return;
        loadingHistory = true; get("more-messages").disabled = true;
        try {
            const page = await request(`conversations/${id}/messages?before=${oldest}`);
            if (!state.isSelected(id, ticket)) return;
            state.page(id, page, "before"); renderMessages("older");
        } catch (error) { if (state.isSelected(id, ticket)) showFailure(error); }
        finally { if (state.isSelected(id, ticket)) { loadingHistory = false; get("more-messages").disabled = false; } }
    }
    function scheduleRead() {
        clearTimeout(readTimer);
        readTimer = setTimeout(async () => {
            const id = state.selectedId;
            if (!id || readBusy || !isOpen()) return;
            const through = state.readCandidate(id, { visible: document.visibilityState === "visible", focused: document.hasFocus(), bottom: isBottom(), loading: !ready || loadingHistory || catchups.has(id) });
            if (!through) return;
            readBusy = true;
            try {
                const receipt = connection?.state === signalR.HubConnectionState.Connected
                    ? await connection.invoke("MarkRead", id, through) : await request(`conversations/${id}/read`, { throughSequence: through });
                state.read(receipt); renderInbox();
            } catch (error) { if (errorCode(error) === 401) signIn(); }
            finally { readBusy = false; }
        }, 250);
    }
    async function submitPending(entry, automatic = false) {
        if (sending || !state.pending.has(entry.command.clientMessageId.toLowerCase()) || sessionEnded) return;
        sending = true; entry.state = "sending"; updateComposer(); renderMessages("bottom"); status("");
        let transient = false;
        try {
            const receipt = connection?.state === signalR.HubConnectionState.Connected
                ? await connection.invoke("SendMessage", entry.conversationId, entry.command)
                : await request(`conversations/${entry.conversationId}/messages`, entry.command);
            if (sessionEnded) return;
            state.merge(receipt.message, true); renderInbox();
            if (entry.conversationId === state.selectedId) { renderMessages("bottom"); await catchup(); }
        } catch (error) {
            if (state.pending.has(entry.command.clientMessageId.toLowerCase())) {
                entry.state = "failed";
                entry.retryable = retryableSend(errorCode(error)); transient = entry.retryable;
                if (entry.conversationId === state.selectedId) showFailure(error, "send your message");
                renderInbox();
            }
        }
        finally { sending = false; updateComposer(); renderMessages(); scheduleRead(); }
        if (transient) {
            if (automatic) { const error = new Error("Message confirmation is pending."); error.pendingConfirmation = true; throw error; }
            recovery.wake();
        }
    }
    get("compose").addEventListener("submit", event => {
        event.preventDefault(); const text = normalizeText(input.value);
        if (send.disabled || !text || text.length > 4000) return;
        const command = { clientMessageId: crypto.randomUUID(), content: text };
        if (sendContext?.orderItemId) command.orderItemId = sendContext.orderItemId;
        else if (sendContext?.productId) command.productId = sendContext.productId;
        const entry = state.beginPending(state.selectedId, command, new Date().toISOString());
        input.value = ""; drafts.delete(state.selectedId); submitPending(entry);
    });
    input.addEventListener("input", updateComposer);
    input.addEventListener("keydown", event => { if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) { event.preventDefault(); if (!send.disabled) get("compose").requestSubmit(); } });
    transcript.addEventListener("scroll", () => { if (isBottom()) get("new-messages").hidden = true; scheduleRead(); });
    get("new-messages").addEventListener("click", () => { transcript.scrollTop = transcript.scrollHeight; get("new-messages").hidden = true; scheduleRead(); });
    get("more-messages").addEventListener("click", olderMessages);
    get("back").addEventListener("click", () => { drafts.set(state.selectedId, input.value); state.select(null); ready = false; root.classList.remove("thread-open"); history.replaceState(null, "", "/Messages"); updateComposer(); syncSubscriptions().catch(showFailure); });
    const resume = () => { scheduleRead(); recovery.wake(); };
    document.addEventListener("visibilitychange", resume); window.addEventListener("focus", resume);
    window.addEventListener("online", resume);
    window.addEventListener("offline", () => { get("connection").textContent = "Offline — waiting for connection"; recovery.wake(); });
    window.addEventListener("pagehide", () => { recovery.stop(); connection?.stop().catch(() => {}); });
    window.addEventListener("pageshow", event => { if (event.persisted) location.reload(); });
    async function reconcile() {
        await token();
        let transportError;
        try { await ensureConnection(); await syncSubscriptions(true); }
        catch (error) { transportError = error; get("connection").textContent = "Live updates paused — retrying…"; }
        await loadInbox(false, true);
        const id = state.selectedId, ticket = state.generation;
        let more = false;
        if (id && isOpen()) {
            const dto = await request(`conversations/${id}`);
            if (!state.isSelected(id, ticket) || !isOpen()) return;
            state.upsert(dto); header(); renderInbox();
            if (state.cache(id).initialized) more = await catchup();
            else {
                await selectConversation(id, dto, sendContext);
                if (sessionEnded || !state.isSelected(id, state.generation)) return;
                if (!ready) throw new Error("History unavailable.");
                return 250;
            }
        }
        for (const entry of [...state.pending.values()]) {
            if (!isOpen() || sessionEnded) return;
            if (entry.state === "failed" && entry.retryable) await submitPending(entry, true);
        }
        if (transportError) { const error = new Error("Live updates unavailable."); error.liveUnavailable = true; error.more = more; throw error; }
        if (!more) { status(""); scheduleRead(); }
        else status("Catching up on messages…");
        return more ? 250 : 15000;
    }
    function refresh() { recovery.wake(); }
    get("refresh-thread").addEventListener("click", refresh);
    if (inboxMode) {
        get("refresh").addEventListener("click", refresh);
        get("more-conversations").addEventListener("click", () => loadInbox(true));
        (async () => {
            try {
                await token(); await loadInbox();
                const initial = Number(root.dataset.conversationId);
                if (initial) {
                    if (!state.conversations.has(initial)) state.upsert(await request(`conversations/${initial}`));
                    await selectConversation(initial);
                } else { await ensureConnection(); await syncSubscriptions(); }
            } catch (error) { showFailure(error); }
            finally { recovery.wake(); }
        })();
        get("retry-load").addEventListener("click", refresh);
        const folderToggle = document.querySelector("[data-folder-toggle]");
        folderToggle?.addEventListener("click", () => {
            const open = root.classList.toggle("folders-open");
            folderToggle.setAttribute("aria-expanded", String(open));
            folderToggle.setAttribute("aria-label", open ? "Hide folders" : "Show folders");
        });
    } else {
        async function openPanel(trigger) {
            lastTrigger = trigger;
            if (!panel.open) panel.showModal();
            document.body.classList.add("chat-modal-open");
            const opening = state.select(null); status(""); ready = false; loadingHistory = false;
            get("message-list").replaceChildren(); get("context").hidden = true; get("thread-empty").hidden = false;
            updateComposer(); get("thread-empty").textContent = "Loading conversation…";
            try {
                await token();
                if (!panel.open || !state.isSelected(null, opening)) return;
                const body = trigger.dataset.chatOrderItemId ? { orderItemId: Number(trigger.dataset.chatOrderItemId) } : { productId: Number(trigger.dataset.chatProductId) };
                const opened = await request("conversations", body);
                if (!panel.open || !state.isSelected(null, opening)) return;
                await selectConversation(opened.conversation.id, opened.conversation, opened.requestedContext);
                input.focus();
            } catch (error) { showFailure(error); }
        }
        get("retry-load").addEventListener("click", () => { if (state.selectedId) refresh(); else if (lastTrigger) openPanel(lastTrigger); });
        for (const trigger of document.querySelectorAll("[data-chat-product-id],[data-chat-order-item-id]")) trigger.addEventListener("click", () => openPanel(trigger));
        panel.querySelector("[data-close-chat]").addEventListener("click", () => panel.close());
        panel.addEventListener("close", () => { document.body.classList.remove("chat-modal-open"); if (state.selectedId) drafts.set(state.selectedId, input.value); state.select(null); ready = false; clearTimeout(readTimer); syncSubscriptions().catch(() => {}); });
        if (new URLSearchParams(location.search).get("message") === "true") {
            const trigger = document.querySelector("[data-chat-product-id]"); if (trigger) openPanel(trigger);
        }
    }
}
