// "Share a secret": symmetric key in the link fragment (or sent separately), one-time reveal.
(function () {
    "use strict";

    const { te, td, toBase64, fromBase64, toBase64Url, fromBase64Url, concat, randomBytes, sha256,
            postJson, errorMessage, showError, requireSecureContext, readTimer, showExpiry } = window.MP;

    // urlKey (32 random bytes, only ever in the URL fragment) [+ PBKDF2(passphrase)] → HKDF →
    //   "encrypt" label: AES-256-GCM key   (never leaves the browser)
    //   "auth" label:    32-byte proof that is sent to the server so it can release the ciphertext

    const PBKDF2_ITERATIONS = 600000;
    // Labels keep the app's former name (MyPastebin): they are part of the key derivation, and changing them
    // would make every secret created before the rename undecryptable.
    const INFO_ENCRYPT = te.encode("mypastebin/v1/encrypt");
    const INFO_AUTH = te.encode("mypastebin/v1/auth");

    async function deriveKeys(urlKey, salt, passphrase) {
        let ikm = urlKey;
        if (passphrase) {
            const pw = await crypto.subtle.importKey("raw", te.encode(passphrase), "PBKDF2", false, ["deriveBits"]);
            const stretched = await crypto.subtle.deriveBits(
                { name: "PBKDF2", salt, iterations: PBKDF2_ITERATIONS, hash: "SHA-256" }, pw, 256);
            ikm = concat(urlKey, new Uint8Array(stretched));
        }
        const base = await crypto.subtle.importKey("raw", ikm, "HKDF", false, ["deriveKey", "deriveBits"]);
        const key = await crypto.subtle.deriveKey(
            { name: "HKDF", hash: "SHA-256", salt, info: INFO_ENCRYPT }, base,
            { name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
        const auth = new Uint8Array(await crypto.subtle.deriveBits(
            { name: "HKDF", hash: "SHA-256", salt, info: INFO_AUTH }, base, 256));
        return { key, auth };
    }

    // ---- create page ----

    const createPanel = document.getElementById("create");
    if (createPanel && requireSecureContext()) {
        const form = document.getElementById("create-form");
        const text = document.getElementById("secret-text");
        const pass = document.getElementById("secret-passphrase");
        const timer = form.querySelector("[data-timer]");
        const btn = document.getElementById("create-btn");
        const error = document.getElementById("create-error");
        const maxBytes = parseInt(createPanel.dataset.maxBytes, 10);

        text.addEventListener("keydown", e => {
            if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
                e.preventDefault();
                form.requestSubmit();
            }
        });

        form.addEventListener("submit", async e => {
            e.preventDefault();
            showError(error, "");

            const plaintext = te.encode(text.value);
            if (text.value.trim() === "") return showError(error, "Secret cannot be empty.");
            if (plaintext.length > maxBytes) return showError(error, `Secret is larger than ${maxBytes / 1024} KB.`);
            const lifetime = readTimer(timer);
            if (lifetime.error) return showError(error, lifetime.error);

            btn.disabled = true;
            btn.textContent = "Encrypting…";
            try {
                const urlKey = randomBytes(32);
                const salt = randomBytes(16);
                const iv = randomBytes(12);
                const { key, auth } = await deriveKeys(urlKey, salt, pass.value);
                const ciphertext = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, plaintext));

                const { status, data } = await postJson("/api/secrets", {
                    ciphertext: toBase64(ciphertext),
                    iv: toBase64(iv),
                    salt: toBase64(salt),
                    authHash: toBase64(await sha256(auth)),
                    hasPassphrase: pass.value.length > 0,
                    lifetimeMinutes: lifetime.minutes,
                });
                if (status !== 200) throw new Error(errorMessage(status, data));

                const link = `${location.origin}/s/${data.id}`;
                const keyText = toBase64Url(urlKey);
                document.getElementById("share-url").value = `${link}#${keyText}`;
                document.getElementById("share-link-only").value = link;
                document.getElementById("share-key").value = keyText;
                document.getElementById("manage-url").value = `${location.origin}/s/${data.id}/manage/${data.manageToken}`;
                showExpiry(document.getElementById("result-expires"), document.getElementById("result-expires-row"),
                           document.getElementById("result-no-timer"), data.expiresUtc);
                document.getElementById("result-passphrase").hidden = pass.value.length === 0;

                text.value = "";
                pass.value = "";
                form.hidden = true;
                document.getElementById("create-result").hidden = false;
                document.getElementById("share-url").select();
            } catch (err) {
                showError(error, err.message || "Encryption failed.");
            } finally {
                btn.disabled = false;
                btn.textContent = "Create secret link";
            }
        });
    }

    // ---- reveal page ----

    const revealPanel = document.getElementById("reveal");
    if (revealPanel && requireSecureContext()) {
        const form = document.getElementById("reveal-form");
        const btn = document.getElementById("reveal-btn");
        const error = document.getElementById("reveal-error");
        const passInput = document.getElementById("reveal-passphrase");
        const keyField = document.getElementById("key-field");
        const keyInput = document.getElementById("reveal-key");
        const id = revealPanel.dataset.id;
        const salt = fromBase64(revealPanel.dataset.salt);

        // Accepts a bare key or a whole pasted link ending in #key; returns null if it isn't a 32-byte key.
        function parseKey(text) {
            let s = (text || "").trim();
            if (s.includes("#")) s = s.slice(s.lastIndexOf("#") + 1);
            if (!/^[A-Za-z0-9_-]{43}$/.test(s)) return null;
            try {
                const bytes = fromBase64Url(s);
                return bytes.length === 32 ? bytes : null;
            } catch {
                return null;
            }
        }

        // Link with key (#fragment) → use it; link without key → ask for the key sent through another channel.
        let urlKey = location.hash.length > 1 ? parseKey(location.hash) : null;
        if (location.hash.length > 1 && !urlKey) {
            showError(error, "The key in this link is damaged. Paste the key below, or ask the sender for the link again.");
        }
        if (!urlKey) {
            keyField.hidden = false;
            keyInput.required = true;
            keyInput.focus();
        }

        form.addEventListener("submit", async e => {
            e.preventDefault();
            showError(error, "");
            if (!urlKey) {
                urlKey = parseKey(keyInput.value);
                if (!urlKey) return showError(error, "That doesn't look like a valid key. It should be 43 characters long.");
            }
            btn.disabled = true;
            btn.textContent = "Decrypting…";
            try {
                const { key, auth } = await deriveKeys(urlKey, salt, passInput ? passInput.value : "");
                const { status, data } = await postJson(`/s/${encodeURIComponent(id)}/reveal`, { authToken: toBase64(auth) });

                if (status === 200) {
                    const plaintext = await crypto.subtle.decrypt(
                        { name: "AES-GCM", iv: fromBase64(data.iv) }, key, fromBase64(data.ciphertext));
                    document.getElementById("revealed-text").value = td.decode(plaintext);
                    document.getElementById("reveal-prompt").hidden = true;
                    document.getElementById("reveal-result").hidden = false;
                    // Drop the key from the address bar and browser history.
                    history.replaceState(null, "", location.pathname);
                    urlKey = null;
                    return;
                }

                // A typed key that turned out wrong must be re-read from the textbox on the next attempt.
                if (!keyField.hidden) urlKey = null;

                if (status === 403 && data) {
                    const left = data.remainingAttempts;
                    showError(error, `${data.error} ${left} attempt${left === 1 ? "" : "s"} left before the secret is destroyed.`);
                } else if (status === 404 || status === 410) {
                    revealPanel.innerHTML = "";
                    const h = document.createElement("h1");
                    h.textContent = "This secret is gone";
                    const p = document.createElement("p");
                    p.className = "muted";
                    p.textContent = data?.error || "It has already been viewed or has expired.";
                    revealPanel.append(h, p);
                    return;
                } else {
                    showError(error, errorMessage(status, data));
                }
            } catch (err) {
                showError(error, "Decryption failed. The link may be damaged.");
            } finally {
                btn.disabled = false;
                btn.textContent = "Reveal secret";
            }
        });
    }
})();
