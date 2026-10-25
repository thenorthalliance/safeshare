// "Request a secret": the requester's browser holds an ECDH P-256 private key (non-extractable, in IndexedDB);
// the sender encrypts to the public key with an ephemeral ECDH key + HKDF + AES-256-GCM (ECIES-style).
(function () {
    "use strict";

    const { te, td, toBase64, fromBase64, concat, randomBytes, sha256, fingerprint,
            postJson, errorMessage, showError, requireSecureContext, readTimer, showExpiry, tickCountdown, store } = window.MP;

    const ECDH = { name: "ECDH", namedCurve: "P-256" };
    // Keeps the former app name on purpose: part of the key derivation (see share.js).
    const INFO_REQUEST = te.encode("mypastebin/v1/request");

    // Both public keys go into the HKDF info, binding the AES key to this exact key exchange.
    async function sealingKey(sharedBits, salt, ephemeralPub, recipientPub) {
        const base = await crypto.subtle.importKey("raw", sharedBits, "HKDF", false, ["deriveKey"]);
        return crypto.subtle.deriveKey(
            { name: "HKDF", hash: "SHA-256", salt, info: concat(INFO_REQUEST, ephemeralPub, recipientPub) },
            base, { name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
    }

    const requestUrl = id => `${location.origin}/r/${id}`;
    const inboxUrl = id => `${location.origin}/r/${id}/inbox`;
    const statusOf = entry => postJson(`/api/requests/${encodeURIComponent(entry.id)}/status`, { token: toBase64(entry.token) });

    function countdownLine(el, prefix, expiresUtc) {
        el.textContent = "";
        if (!expiresUtc) {
            el.textContent = `${prefix} never (no timer set).`;
            return;
        }
        const t = document.createElement("time");
        t.setAttribute("datetime", expiresUtc);
        t.dataset.countdown = "";
        tickCountdown(t);
        el.append(`${prefix} in `, t, ".");
    }

    // ---- requester: create a request ----

    const newPanel = document.getElementById("new-request");
    if (newPanel && requireSecureContext()) {
        const form = document.getElementById("request-form");
        const description = document.getElementById("request-description");
        const timer = form.querySelector("[data-timer]");
        const btn = document.getElementById("request-btn");
        const error = document.getElementById("request-error");

        form.addEventListener("submit", async e => {
            e.preventDefault();
            showError(error, "");
            const lifetime = readTimer(timer);
            if (lifetime.error) return showError(error, lifetime.error);

            try {
                await store.all();
            } catch {
                return showError(error, "This browser can't store keys (a private window, or site data is blocked). Use a normal window.");
            }

            btn.disabled = true;
            btn.textContent = "Creating key pair…";
            try {
                // Private key is non-extractable: not even script on this page can read its bytes.
                const pair = await crypto.subtle.generateKey(ECDH, false, ["deriveBits"]);
                const publicKey = new Uint8Array(await crypto.subtle.exportKey("raw", pair.publicKey));
                const token = randomBytes(32);

                const { status, data } = await postJson("/api/requests", {
                    publicKey: toBase64(publicKey),
                    tokenHash: toBase64(await sha256(token)),
                    description: description.value.trim() || null,
                    lifetimeMinutes: lifetime.minutes,
                });
                if (status !== 200) throw new Error(errorMessage(status, data));

                const code = await fingerprint(publicKey);
                await store.put({
                    id: data.id,
                    privateKey: pair.privateKey,
                    publicKey,
                    token,
                    description: description.value.trim() || null,
                    fingerprint: code,
                    createdUtc: new Date().toISOString(),
                });
                // Ask the browser not to evict this site's storage under disk pressure.
                navigator.storage?.persist?.().catch(() => { });

                document.getElementById("request-url").value = requestUrl(data.id);
                document.getElementById("request-fingerprint").textContent = code;
                document.getElementById("inbox-link").href = `/r/${data.id}/inbox`;
                showExpiry(document.getElementById("request-expires"), document.getElementById("request-expires-row"),
                           document.getElementById("request-no-timer"), data.expiresUtc);
                form.hidden = true;
                document.getElementById("request-result").hidden = false;
            } catch (err) {
                showError(error, err.message || "Could not create the request.");
            } finally {
                btn.disabled = false;
                btn.textContent = "Create request";
            }
        });
    }

    // ---- requester: list of requests in this browser ----

    const minePanel = document.getElementById("my-requests");
    if (minePanel) {
        (async () => {
            let entries = [];
            try { entries = await store.all(); } catch { }
            entries.sort((a, b) => b.createdUtc.localeCompare(a.createdUtc));
            document.getElementById("requests-empty").hidden = entries.length > 0;
            const table = document.getElementById("requests-table");
            table.hidden = entries.length === 0;
            const body = table.querySelector("tbody");

            for (const entry of entries) {
                const tr = document.createElement("tr");
                const name = document.createElement("td");
                const link = document.createElement("a");
                link.href = `/r/${entry.id}/inbox`;
                link.textContent = entry.description || entry.id;
                name.append(link);
                const created = document.createElement("td");
                created.textContent = new Date(entry.createdUtc).toLocaleString();
                const state = document.createElement("td");
                state.textContent = "…";
                const actions = document.createElement("td");
                tr.append(name, created, state, actions);
                body.append(tr);

                statusOf(entry).then(({ status, data }) => {
                    if (status === 200) {
                        state.textContent = data.status === "fulfilled" ? "Secret arrived" : "Waiting";
                    } else if (status === 404) {
                        state.textContent = "Closed";
                        const remove = document.createElement("button");
                        remove.className = "btn";
                        remove.textContent = "Remove";
                        remove.addEventListener("click", async () => {
                            await store.remove(entry.id);
                            tr.remove();
                        });
                        actions.append(remove);
                    } else {
                        state.textContent = errorMessage(status, data);
                    }
                });
            }
        })();
    }

    // ---- requester: one request (wait, retrieve, decrypt) ----

    const inboxPanel = document.getElementById("inbox");
    if (inboxPanel && requireSecureContext()) {
        const id = inboxPanel.dataset.id;
        const error = document.getElementById("inbox-error");
        const cancelBtn = document.getElementById("cancel-btn");
        const retrieveBtn = document.getElementById("retrieve-btn");
        const states = ["pending", "fulfilled", "gone", "revealed"];
        let entry = null;
        let poll = null;
        // Set once the page reaches a final state (revealed or gone); later status answers must not overwrite it.
        let finished = false;

        const show = state => states.forEach(s => (document.getElementById(`state-${s}`).hidden = s !== state));

        function stopPolling() {
            clearInterval(poll);
            poll = null;
        }

        async function gone(message) {
            finished = true;
            stopPolling();
            show("gone");
            cancelBtn.hidden = true;
            document.getElementById("gone-text").textContent = message;
            // The private key is useless now; don't leave it lying around.
            await store.remove(id).catch(() => { });
        }

        // Returns true while the request is still waiting for the sender.
        async function refresh() {
            if (finished) return false;
            const { status, data } = await statusOf(entry);
            if (finished) return false;   // revealed while this call was in flight
            if (status === 200 && data.status === "pending") {
                show("pending");
                cancelBtn.hidden = false;
                countdownLine(document.getElementById("pending-expiry"), "This request auto-burns", data.expiresUtc);
                return true;
            }
            stopPolling();
            if (status === 200) {
                show("fulfilled");
                cancelBtn.hidden = false;
                countdownLine(document.getElementById("fulfilled-expiry"), "Unopened, it auto-burns", data.expiresUtc);
            } else if (status === 404) {
                await gone(data?.error || "This request is closed.");
            } else {
                showError(error, errorMessage(status, data));
                return true;   // transient error (e.g. rate limit): keep polling
            }
            return false;
        }

        (async () => {
            try { entry = await store.get(id); } catch { }
            if (!entry) {
                document.getElementById("inbox-missing").hidden = false;
                return;
            }
            document.getElementById("inbox-main").hidden = false;
            document.getElementById("inbox-title").textContent = entry.description ? `Request: ${entry.description}` : "Your request";
            document.getElementById("inbox-description").textContent =
                `Created ${new Date(entry.createdUtc).toLocaleString()} in this browser.`;
            document.getElementById("inbox-fingerprint").textContent = entry.fingerprint;
            document.getElementById("inbox-request-url").value = requestUrl(id);
            // Only poll while waiting for the sender. Starting the timer unconditionally after the first check
            // kept it running after a reveal, and the next "gone" answer then replaced the revealed secret.
            if (await refresh()) {
                poll = setInterval(() => refresh().catch(() => { }), 10000);
            }
        })();

        retrieveBtn.addEventListener("click", async () => {
            showError(error, "");
            retrieveBtn.disabled = true;
            retrieveBtn.textContent = "Decrypting…";
            try {
                const { status, data } = await postJson(`/api/requests/${encodeURIComponent(id)}/retrieve`, { token: toBase64(entry.token) });
                if (status === 404) return await gone(data?.error || "There is no secret waiting.");
                if (status !== 200) throw new Error(errorMessage(status, data));

                const ephemeralPub = fromBase64(data.ephemeralPublicKey);
                const ephemeralKey = await crypto.subtle.importKey("raw", ephemeralPub, ECDH, false, []);
                const shared = await crypto.subtle.deriveBits({ name: "ECDH", public: ephemeralKey }, entry.privateKey, 256);
                const key = await sealingKey(shared, fromBase64(data.salt), ephemeralPub, new Uint8Array(entry.publicKey));
                const plaintext = await crypto.subtle.decrypt(
                    { name: "AES-GCM", iv: fromBase64(data.iv), additionalData: te.encode(id) }, key, fromBase64(data.ciphertext));

                document.getElementById("inbox-secret").value = td.decode(plaintext);
                finished = true;
                stopPolling();
                show("revealed");
                cancelBtn.hidden = true;
                await store.remove(id);
            } catch (err) {
                showError(error, err.message || "Decryption failed.");
            } finally {
                retrieveBtn.disabled = false;
                retrieveBtn.textContent = "Decrypt and show";
            }
        });

        // Two-step button instead of confirm(), which isn't reliable everywhere.
        cancelBtn.addEventListener("click", async () => {
            if (!cancelBtn.dataset.armed) {
                cancelBtn.dataset.armed = "1";
                cancelBtn.textContent = "Click again to cancel the request";
                setTimeout(() => {
                    delete cancelBtn.dataset.armed;
                    cancelBtn.textContent = "Cancel request";
                }, 4000);
                return;
            }
            const { status, data } = await postJson(`/api/requests/${encodeURIComponent(id)}/cancel`, { token: toBase64(entry.token) });
            if (status === 200 || status === 404) await gone("The request was cancelled. Anything sent for it has been deleted.");
            else showError(error, errorMessage(status, data));
        });
    }

    // ---- sender: fill a request ----

    const fulfillPanel = document.getElementById("fulfill");
    if (fulfillPanel && requireSecureContext()) {
        const id = fulfillPanel.dataset.id;
        const recipientPub = fromBase64(fulfillPanel.dataset.publicKey);
        const maxBytes = parseInt(fulfillPanel.dataset.maxBytes, 10);
        const form = document.getElementById("fulfill-form");
        const text = document.getElementById("fulfill-text");
        const verified = document.getElementById("fulfill-verified");
        const timer = form.querySelector("[data-timer]");
        const btn = document.getElementById("fulfill-btn");
        const error = document.getElementById("fulfill-error");

        fingerprint(recipientPub).then(code => (document.getElementById("fulfill-fingerprint").textContent = code));

        form.addEventListener("submit", async e => {
            e.preventDefault();
            showError(error, "");

            const plaintext = te.encode(text.value);
            if (text.value.trim() === "") return showError(error, "Secret cannot be empty.");
            if (plaintext.length > maxBytes) return showError(error, `Secret is larger than ${maxBytes / 1024} KB.`);
            if (!verified.checked) return showError(error, "Confirm the security code with the requester first.");
            const lifetime = readTimer(timer);
            if (lifetime.error) return showError(error, lifetime.error);

            btn.disabled = true;
            btn.textContent = "Encrypting…";
            try {
                const recipientKey = await crypto.subtle.importKey("raw", recipientPub, ECDH, false, []);
                const ephemeral = await crypto.subtle.generateKey(ECDH, false, ["deriveBits"]);
                const ephemeralPub = new Uint8Array(await crypto.subtle.exportKey("raw", ephemeral.publicKey));
                const shared = await crypto.subtle.deriveBits({ name: "ECDH", public: recipientKey }, ephemeral.privateKey, 256);
                const salt = randomBytes(16);
                const iv = randomBytes(12);
                const key = await sealingKey(shared, salt, ephemeralPub, recipientPub);
                const ciphertext = new Uint8Array(await crypto.subtle.encrypt(
                    { name: "AES-GCM", iv, additionalData: te.encode(id) }, key, plaintext));

                const { status, data } = await postJson(`/api/requests/${encodeURIComponent(id)}/fulfill`, {
                    ephemeralPublicKey: toBase64(ephemeralPub),
                    salt: toBase64(salt),
                    iv: toBase64(iv),
                    ciphertext: toBase64(ciphertext),
                    lifetimeMinutes: lifetime.minutes,
                });
                if (status !== 200) throw new Error(errorMessage(status, data));

                text.value = "";
                form.hidden = true;
                showExpiry(document.getElementById("fulfill-expires"), document.getElementById("fulfill-expires-row"),
                           document.getElementById("fulfill-no-timer"), data.expiresUtc);
                document.getElementById("fulfill-result").hidden = false;
            } catch (err) {
                showError(error, err.message || "Encryption failed.");
            } finally {
                btn.disabled = false;
                btn.textContent = "Encrypt and send";
            }
        });
    }
})();
