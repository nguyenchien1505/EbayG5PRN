// The loop owns scheduling only; SQL reconciliation remains in the caller.
export const retryableSend = code => code === 0 || code === 429 || code >= 500;
export const recoveryDelay = (failures, random = Math.random) =>
    Math.round(Math.min(30000, 1000 * 2 ** Math.min(5, Math.max(0, failures - 1))) * (0.75 + 0.25 * random()));

export class ChatRecoveryLoop {
    constructor(action, { active = () => true, failed = () => {}, schedule = (callback, delay) => setTimeout(callback, delay), cancel = id => clearTimeout(id), random = Math.random } = {}) {
        Object.assign(this, { action, active, failed, schedule, cancel, random });
        this.failures = 0; this.running = false; this.stopped = false; this.again = false; this.timer = null;
    }
    wake() {
        if (this.stopped) return;
        this.cancel(this.timer); this.timer = null;
        if (this.running) { this.again = true; return; }
        if (this.active()) this.timer = this.schedule(() => this.run(), 0);
    }
    stop() { this.stopped = true; this.cancel(this.timer); this.timer = null; this.again = false; }
    async run() {
        this.timer = null;
        if (this.stopped || !this.active() || this.running) return;
        this.running = true;
        let delay = 15000;
        try { delay = await this.action() ?? 15000; this.failures = 0; }
        catch (error) { delay = recoveryDelay(++this.failures, this.random); this.failed(error); }
        finally {
            this.running = false;
            if (!this.stopped && this.active()) this.timer = this.schedule(() => this.run(), this.again ? 0 : delay);
            this.again = false;
        }
    }
}
