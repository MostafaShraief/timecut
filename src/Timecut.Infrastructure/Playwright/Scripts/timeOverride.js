// Timecut Time Override Script
// Injected into pages to control animation timing deterministically
// This replaces Date, performance.now, requestAnimationFrame, and media APIs

window.__timecut = { currentTime: 0, fps: 30 };

// 1. Math.random determinism
let seed = 1;
window.Math.random = () => {
    const x = Math.sin(seed++) * 10000;
    return x - Math.floor(x);
};

// 2. Date override
const _Date = Date;
window.Date = class extends _Date {
    constructor(...args) {
        if (args.length === 0) return new _Date(window.__timecut.currentTime * 1000);
        return new _Date(...args);
    }
    static now() { return window.__timecut.currentTime * 1000; }
};

// 3. performance.now override
const _perf = window.performance;
window.performance = { ..._perf, now: () => window.__timecut.currentTime * 1000 };

// 4. requestAnimationFrame override
window.__rafs = {};
window.__rafId = 0;
window.requestAnimationFrame = (cb) => {
    window.__rafId++;
    window.__rafs[window.__rafId] = cb;
    return window.__rafId;
};
window.cancelAnimationFrame = (id) => {
    delete window.__rafs[id];
};

// 5. setTimeout/setInterval overrides for deterministic behavior
const _setTimeout = window.setTimeout;
const _setInterval = window.setInterval;
const _clearTimeout = window.clearTimeout;
const _clearInterval = window.clearInterval;

let pendingTimeouts = {};
let timeoutId = 1000000;

window.setTimeout = (cb, delay, ...args) => {
    const id = timeoutId++;
    const targetTime = window.__timecut.currentTime * 1000 + (delay || 0);
    pendingTimeouts[id] = { cb, targetTime, args, type: 'timeout' };
    return id;
};

window.setInterval = (cb, delay, ...args) => {
    const id = timeoutId++;
    const interval = delay || 0;
    const targetTime = window.__timecut.currentTime * 1000 + interval;
    pendingTimeouts[id] = { cb, targetTime, args, type: 'interval', interval };
    return id;
};

window.clearTimeout = (id) => {
    delete pendingTimeouts[id];
};

window.clearInterval = (id) => {
    delete pendingTimeouts[id];
};

// 6. Media & Animation freezing
const freezeMedia = () => {
    try {
        if (document.timeline && document.timeline.currentTime !== undefined) {
            try { document.timeline.currentTime = window.__timecut.currentTime * 1000; } catch(e) {}
        }
        document.querySelectorAll('video, audio').forEach(v => {
            try {
                v.pause();
                v.currentTime = window.__timecut.currentTime;
            } catch(e) {}
        });
        document.querySelectorAll('svg').forEach(svg => {
            try {
                if (svg.pauseAnimations) svg.pauseAnimations();
                if (svg.setCurrentTime) svg.setCurrentTime(window.__timecut.currentTime);
            } catch(e) {}
        });
    } catch(e) {}
};

if (typeof document !== 'undefined') {
    document.addEventListener('DOMContentLoaded', freezeMedia);
    freezeMedia();
}

// 7. Master update function called per frame
window.__updateTime = (t) => {
    window.__timecut.currentTime = t;
    freezeMedia();

    // Process pending timeouts/intervals
    const currentMs = t * 1000;
    const timeoutsToRun = [];
    for (const [id, entry] of Object.entries(pendingTimeouts)) {
        if (currentMs >= entry.targetTime) {
            timeoutsToRun.push({ id, ...entry });
        }
    }
    for (const entry of timeoutsToRun) {
        if (entry.type === 'timeout') {
            delete pendingTimeouts[entry.id];
        } else if (entry.type === 'interval') {
            pendingTimeouts[entry.id].targetTime = currentMs + entry.interval;
        }
        try { entry.cb(...entry.args); } catch(e) {}
    }

    // Fire requestAnimationFrame callbacks
    const currentRafs = window.__rafs;
    window.__rafs = {};
    Object.values(currentRafs).forEach(cb => {
        if (cb) {
            try { cb(t * 1000); } catch(e) {}
        }
    });
};
