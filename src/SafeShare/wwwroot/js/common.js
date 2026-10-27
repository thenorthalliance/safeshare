// Helpers shared by share.js and request.js, exposed as window.MP.
(function () {
    "use strict";

    const te = new TextEncoder();
    const td = new TextDecoder();

    // ---- encoding ----

    function toBase64(bytes) {
        let bin = "";
        for (let i = 0; i < bytes.length; i += 0x8000) {
            bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
        }
        return btoa(bin);
    }

    function fromBase64(str) {
        const bin = atob(str);
        const out = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
        return out;
    }

    const toBase64Url = bytes => toBase64(bytes).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    const fromBase64Url = str => fromBase64(str.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((str.length + 3) % 4));

    function concat(...parts) {
        const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
        let offset = 0;
        for (const p of parts) {
            out.set(p, offset);
            offset += p.length;
        }
        return out;
    }

    const randomBytes = n => crypto.getRandomValues(new Uint8Array(n));
    const sha256 = async bytes => new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));

    // Human-comparable code for a public key: first 80 bits of SHA-256 in Crockford base32, e.g. "7KQ2-M9XD-4TRA-0GHF".
    async function fingerprint(publicKeyRaw) {
        const alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        const hash = (await sha256(publicKeyRaw)).subarray(0, 10);
        let bits = 0, value = 0, out = "";
        for (const b of hash) {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5) {
                out += alphabet[(value >>> (bits - 5)) & 31];
                bits -= 5;
            }
        }
        return out.match(/.{4}/g).join("-");
    }

    // ---- server calls ----

    function antiforgeryToken() {
        const input = document.querySelector("input[name=__RequestVerificationToken]");
        return input ? input.value : "";
    }

    async function postJson(url, body) {
        const res = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json", "RequestVerificationToken": antiforgeryToken() },
            body: JSON.stringify(body),
            credentials: "same-origin",
            cache: "no-store",
        });
        let data = null;
        try { data = await res.json(); } catch { }
        return { status: res.status, data };
    }

    function errorMessage(status, data) {
        if (data && data.error) return data.error;
        if (status === 429) return "Too many requests. Wait a minute and try again.";
        return `Server error (${status}).`;
    }

    // ---- UI ----

    function showError(el, message) {
        el.textContent = message || "";
        el.hidden = !message;
    }

    function requireSecureContext() {
        if (window.crypto && crypto.subtle) return true;
        document.getElementById("insecure-context").hidden = false;
        document.querySelectorAll("button[type=submit], .btn.primary").forEach(b => (b.disabled = true));
        return false;
    }

    // Auto-burn timer field (Views/Shared/_TimerField.cshtml). Returns minutes, or null for "no timer".
    function readTimer(fieldset) {
        const enabled = fieldset.querySelector(".timer-enabled").checked;
        if (!enabled) return { minutes: null };
        const amount = Number(fieldset.querySelector(".timer-amount").value);
        const unit = Number(fieldset.querySelector(".timer-unit").value);
        if (!Number.isInteger(amount) || amount < 1) return { error: "Enter a whole number for the timer." };
        const minutes = amount * unit;
        const max = Number(fieldset.dataset.maxMinutes);
        if (minutes > max) return { error: `The timer can't be longer than ${formatMinutes(max)}.` };
        return { minutes };
    }

    function formatMinutes(m) {
        if (m % 1440 === 0) return `${m / 1440} day(s)`;
        if (m % 60 === 0) return `${m / 60} hour(s)`;
        return `${m} minute(s)`;
    }

    document.querySelectorAll("[data-timer]").forEach(fs => {
        const toggle = fs.querySelector(".timer-enabled");
        const apply = () => fs.querySelectorAll(".timer-amount, .timer-unit").forEach(i => (i.disabled = !toggle.checked));
        toggle.addEventListener("change", apply);
        apply();
    });

    // Shows the countdown row (with the given UTC time) or the "no timer" row.
    function showExpiry(timeEl, rowEl, noTimerEl, expiresUtc) {
        rowEl.hidden = !expiresUtc;
        noTimerEl.hidden = !!expiresUtc;
        if (expiresUtc) {
            timeEl.setAttribute("datetime", expiresUtc);
            tickCountdown(timeEl);
        }
    }

    // Show/hide toggles for password fields: <button data-reveal="#input" aria-pressed="false">.
    document.querySelectorAll("[data-reveal]").forEach(btn => {
        const target = document.querySelector(btn.dataset.reveal);
        if (!target) return;
        btn.addEventListener("click", () => {
            const show = target.type === "password";
            target.type = show ? "text" : "password";
            btn.setAttribute("aria-pressed", String(show));
        });
    });

    document.querySelectorAll("[data-copy]").forEach(btn => {
        btn.addEventListener("click", async () => {
            const target = document.querySelector(btn.dataset.copy);
            if (!target) return;
            const text = "value" in target ? target.value : target.textContent;
            try {
                await navigator.clipboard.writeText(text);
            } catch {
                target.select?.();
                document.execCommand("copy");
            }
            const original = btn.textContent;
            btn.textContent = "Copied!";
            setTimeout(() => (btn.textContent = original), 1500);
        });
    });

    // ---- time display ----

    const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
    const units = [["year", 31536000], ["month", 2592000], ["week", 604800], ["day", 86400], ["hour", 3600], ["minute", 60]];

    function localizeTime(el) {
        const date = new Date(el.getAttribute("datetime"));
        if (isNaN(date)) return;
        el.title = date.toLocaleString();
        const diff = (date - Date.now()) / 1000;
        const abs = Math.abs(diff);
        const unit = units.find(([, secs]) => abs >= secs);
        el.textContent = unit ? rtf.format(Math.round(diff / unit[1]), unit[0]) : (diff < 0 ? "just now" : "in a moment");
    }

    function tickCountdown(el) {
        const date = new Date(el.getAttribute("datetime"));
        if (isNaN(date)) return;
        el.title = date.toLocaleString();
        let s = Math.max(0, Math.floor((date - Date.now()) / 1000));
        if (s === 0) {
            el.textContent = "0:00:00 (burned)";
            el.classList.add("expired");
            return;
        }
        const d = Math.floor(s / 86400); s %= 86400;
        const h = Math.floor(s / 3600); s %= 3600;
        const m = Math.floor(s / 60); s %= 60;
        const pad = n => String(n).padStart(2, "0");
        el.textContent = `${d ? d + "d " : ""}${d ? pad(h) : h}:${pad(m)}:${pad(s)}`;
    }

    document.querySelectorAll("time[data-relative][datetime]").forEach(localizeTime);
    const tickAll = () => document.querySelectorAll("time[data-countdown][datetime]").forEach(tickCountdown);
    tickAll();
    setInterval(tickAll, 1000);

    // ---- IndexedDB: requester key pairs (CryptoKey objects are stored as-is and stay non-extractable) ----

    function idbOpen() {
        return new Promise((resolve, reject) => {
            // Store name predates the rename to SafeShare; renaming it would orphan the private keys of open requests.
            const req = indexedDB.open("mypastebin", 1);
            req.onupgradeneeded = () => req.result.createObjectStore("requests", { keyPath: "id" });
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
    }

    async function idb(mode, fn) {
        const db = await idbOpen();
        try {
            return await new Promise((resolve, reject) => {
                const tx = db.transaction("requests", mode);
                const req = fn(tx.objectStore("requests"));
                tx.oncomplete = () => resolve(req.result);
                tx.onerror = () => reject(tx.error);
                tx.onabort = () => reject(tx.error);
            });
        } finally {
            db.close();
        }
    }

    const store = {
        put: value => idb("readwrite", s => s.put(value)),
        get: id => idb("readonly", s => s.get(id)),
        all: () => idb("readonly", s => s.getAll()),
        remove: id => idb("readwrite", s => s.delete(id)),
    };

    window.MP = {
        te, td, toBase64, fromBase64, toBase64Url, fromBase64Url, concat, randomBytes, sha256, fingerprint,
        postJson, errorMessage, showError, requireSecureContext, readTimer, showExpiry, tickCountdown, store,
    };
})();
