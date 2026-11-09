// "Share an artifact": a password-protected HTML page that can be opened many times until it expires or is deleted.
(function () {
    "use strict";

    const { te, td, toBase64, fromBase64, randomBytes, sha256, generatePassphrase,
            postJson, errorMessage, showError, requireSecureContext, readTimer } = window.MP;

    // password → PBKDF2-SHA256 (600k, salt) → HKDF-SHA256 (salt) →
    //   "encrypt" label: AES-256-GCM key   (never leaves the browser)
    //   "auth" label:    32-byte proof sent to the server, so it can count wrong passwords before releasing ciphertext
    // Plaintext: gzip(UTF-8 JSON { v: 1, title, html }).

    const PBKDF2_ITERATIONS = 600000;
    const MIN_PASSWORD_LENGTH = 12;
    const INFO_ENCRYPT = te.encode("safeshare/v1/artifact/encrypt");
    const INFO_AUTH = te.encode("safeshare/v1/artifact/auth");
    const FRAME_URL = "/a/_frame";

    // Trimmed and NFC-normalized, so a password copied with stray spaces or typed on another OS still matches.
    const normalizePassword = p => (p || "").trim().normalize("NFC");

    async function deriveKeys(password, salt) {
        const pw = await crypto.subtle.importKey("raw", te.encode(password), "PBKDF2", false, ["deriveBits"]);
        const stretched = await crypto.subtle.deriveBits(
            { name: "PBKDF2", salt, iterations: PBKDF2_ITERATIONS, hash: "SHA-256" }, pw, 256);
        const base = await crypto.subtle.importKey("raw", stretched, "HKDF", false, ["deriveKey", "deriveBits"]);
        const key = await crypto.subtle.deriveKey(
            { name: "HKDF", hash: "SHA-256", salt, info: INFO_ENCRYPT }, base,
            { name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
        const auth = new Uint8Array(await crypto.subtle.deriveBits(
            { name: "HKDF", hash: "SHA-256", salt, info: INFO_AUTH }, base, 256));
        return { key, auth };
    }

    const pipe = async (bytes, transform) =>
        new Uint8Array(await new Response(new Blob([bytes]).stream().pipeThrough(transform)).arrayBuffer());
    const gzip = bytes => pipe(bytes, new CompressionStream("gzip"));
    const gunzip = bytes => pipe(bytes, new DecompressionStream("gzip"));

    // ---- this browser's list of shared artifacts (link and delete page only, never the password) ----

    const LIST_KEY = "safeshare.artifacts";

    const list = {
        load() {
            try {
                const items = JSON.parse(localStorage.getItem(LIST_KEY) || "[]");
                return Array.isArray(items) ? items.filter(i => new Date(i.expiresUtc) > new Date()) : [];
            } catch {
                return [];
            }
        },
        save(items) {
            try { localStorage.setItem(LIST_KEY, JSON.stringify(items)); } catch { }
        },
        add(item) { this.save([item, ...this.load().filter(i => i.id !== item.id)]); },
        remove(id) { this.save(this.load().filter(i => i.id !== id)); },
    };

    function renderList() {
        const wrap = document.getElementById("artifact-list");
        const body = wrap.querySelector("tbody");
        const items = list.load();
        body.replaceChildren();
        wrap.hidden = items.length === 0;

        for (const item of items) {
            const tr = document.createElement("tr");

            const name = document.createElement("td");
            const link = document.createElement("a");
            link.href = item.url;
            link.target = "_blank";
            link.rel = "noopener";
            link.textContent = item.title || "Untitled";
            name.append(link);

            const expires = document.createElement("td");
            const time = document.createElement("time");
            time.setAttribute("datetime", item.expiresUtc);
            time.dataset.countdown = "";
            const cd = document.createElement("span");
            cd.className = "countdown";
            cd.append("in ", time);
            expires.append(cd);
            MP.tickCountdown(time);

            const actions = document.createElement("td");
            actions.className = "table-actions";
            const copy = document.createElement("button");
            copy.type = "button";
            copy.className = "btn";
            copy.textContent = "Copy link";
            copy.addEventListener("click", async () => {
                try { await navigator.clipboard.writeText(item.url); copy.textContent = "Copied!"; } catch { copy.textContent = "Copy failed"; }
                setTimeout(() => (copy.textContent = "Copy link"), 1500);
            });
            const manage = document.createElement("a");
            manage.className = "btn";
            manage.href = item.manageUrl;
            manage.textContent = "Manage";
            const forget = document.createElement("button");
            forget.type = "button";
            forget.className = "btn";
            forget.textContent = "Forget";
            forget.title = "Remove from this list. The artifact itself stays online.";
            forget.addEventListener("click", () => { list.remove(item.id); renderList(); });
            actions.append(copy, manage, forget);

            tr.append(name, expires, actions);
            body.append(tr);
        }
    }

    // ---- create page ----

    const createPanel = document.getElementById("artifact-create");
    if (createPanel) {
        // Coming back from "Delete now" on the manage page.
        const deleted = new URLSearchParams(location.search).get("deleted");
        if (deleted) {
            list.remove(deleted);
            history.replaceState(null, "", location.pathname);
        }
        renderList();
    }

    if (createPanel && requireSecureContext()) {
        const form = document.getElementById("artifact-form");
        const title = document.getElementById("artifact-title");
        const html = document.getElementById("artifact-html");
        const file = document.getElementById("artifact-file");
        const fileName = document.getElementById("artifact-file-name");
        const pass = document.getElementById("artifact-password");
        const timer = form.querySelector("[data-timer]");
        const btn = document.getElementById("artifact-btn");
        const error = document.getElementById("artifact-error");
        const maxBytes = parseInt(createPanel.dataset.maxBytes, 10);

        // The creator has to pass the password on, so it starts out generated and visible.
        const reveal = form.querySelector('[data-reveal="#artifact-password"]');
        const newPassword = () => {
            pass.value = generatePassphrase();
            if (pass.type === "password") reveal.click();
        };
        newPassword();
        document.getElementById("artifact-generate").addEventListener("click", () => {
            newPassword();
            pass.focus();
            pass.select();
        });

        async function loadFile(f) {
            if (!f) return;
            html.value = await f.text();
            fileName.textContent = `${f.name} (${Math.ceil(f.size / 1024)} KB)`;
            if (!title.value) title.value = f.name.replace(/\.html?$/i, "");
        }
        file.addEventListener("change", () => loadFile(file.files[0]));
        html.addEventListener("dragover", e => e.preventDefault());
        html.addEventListener("drop", e => {
            if (!e.dataTransfer.files.length) return;
            e.preventDefault();
            loadFile(e.dataTransfer.files[0]);
        });

        form.addEventListener("submit", async e => {
            e.preventDefault();
            showError(error, "");

            const password = normalizePassword(pass.value);
            if (html.value.trim() === "") return showError(error, "Paste the HTML code or choose a file.");
            if (password.length < MIN_PASSWORD_LENGTH)
                return showError(error, `The password must be at least ${MIN_PASSWORD_LENGTH} characters long.`);
            const lifetime = readTimer(timer);
            if (lifetime.error) return showError(error, lifetime.error);
            if (lifetime.minutes === null) return showError(error, "Choose how long the artifact stays available.");

            btn.disabled = true;
            btn.textContent = "Encrypting…";
            try {
                const titleText = title.value.trim();
                const plaintext = await gzip(te.encode(JSON.stringify({ v: 1, title: titleText, html: html.value })));
                if (plaintext.length > maxBytes)
                    throw new Error(`The artifact is larger than ${Math.round(maxBytes / 1048576)} MB after compression.`);

                const salt = randomBytes(16);
                const iv = randomBytes(12);
                const { key, auth } = await deriveKeys(password, salt);
                const ciphertext = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, plaintext));

                btn.textContent = "Uploading…";
                const { status, data } = await postJson("/api/artifacts", {
                    ciphertext: toBase64(ciphertext),
                    iv: toBase64(iv),
                    salt: toBase64(salt),
                    authHash: toBase64(await sha256(auth)),
                    lifetimeMinutes: lifetime.minutes,
                });
                if (status === 413) throw new Error("The artifact is too large for the server.");
                if (status !== 200) throw new Error(errorMessage(status, data));

                const url = `${location.origin}/a/${data.id}`;
                const manageUrl = `${location.origin}/a/${data.id}/manage/${data.manageToken}`;
                document.getElementById("artifact-url").value = url;
                document.getElementById("artifact-result-password").value = password;
                document.getElementById("artifact-manage-url").value = manageUrl;
                const expires = document.getElementById("artifact-expires");
                expires.setAttribute("datetime", data.expiresUtc);
                MP.tickCountdown(expires);

                list.add({ id: data.id, title: titleText, url, manageUrl, expiresUtc: data.expiresUtc });
                renderList();

                html.value = "";
                pass.value = "";
                form.hidden = true;
                document.getElementById("artifact-result").hidden = false;
                document.getElementById("artifact-url").select();
            } catch (err) {
                showError(error, err.message || "Encryption failed.");
            } finally {
                btn.disabled = false;
                btn.textContent = "Create shared link";
            }
        });
    }

    // ---- viewer ----

    const openPanel = document.getElementById("artifact-open");
    if (openPanel && requireSecureContext()) {
        const form = document.getElementById("artifact-unlock");
        const pass = document.getElementById("artifact-open-password");
        const btn = document.getElementById("artifact-open-btn");
        const error = document.getElementById("artifact-open-error");
        const viewer = document.getElementById("artifact-viewer");
        const frame = document.getElementById("artifact-frame");
        const id = openPanel.dataset.id;
        const salt = fromBase64(openPanel.dataset.salt);

        function showGone(message) {
            openPanel.replaceChildren();
            const h = document.createElement("h1");
            h.textContent = "This shared page is gone";
            const p = document.createElement("p");
            p.className = "muted";
            p.textContent = message || "It has expired or was deleted.";
            openPanel.append(h, p);
        }

        // The frame is sandboxed onto an opaque origin, so it can only be addressed with "*". Send the HTML exactly
        // once, and only to the frame's own window after its shell says it's ready (before the artifact can run).
        function render(payload) {
            const onMessage = e => {
                if (e.source !== frame.contentWindow || !e.data || e.data.type !== "safeshare-artifact-ready") return;
                window.removeEventListener("message", onMessage);
                frame.contentWindow.postMessage({ type: "safeshare-artifact", html: payload.html }, "*");
            };
            window.addEventListener("message", onMessage);

            const titleText = payload.title || "Shared page";
            document.getElementById("artifact-viewer-title").textContent = titleText;
            document.title = titleText;
            document.body.classList.add("artifact-mode");
            viewer.hidden = false;
            frame.src = FRAME_URL;
        }

        document.getElementById("artifact-fullscreen").addEventListener("click", () => {
            frame.requestFullscreen?.().catch(() => { });
        });

        form.addEventListener("submit", async e => {
            e.preventDefault();
            showError(error, "");
            const password = normalizePassword(pass.value);
            if (!password) return showError(error, "Enter the password.");

            btn.disabled = true;
            btn.textContent = "Decrypting…";
            try {
                const { key, auth } = await deriveKeys(password, salt);
                const { status, data } = await postJson(`/a/${encodeURIComponent(id)}/open`, { authToken: toBase64(auth) });

                if (status === 200) {
                    const plaintext = await crypto.subtle.decrypt(
                        { name: "AES-GCM", iv: fromBase64(data.iv) }, key, fromBase64(data.ciphertext));
                    const payload = JSON.parse(td.decode(await gunzip(new Uint8Array(plaintext))));
                    pass.value = "";
                    render(payload);
                } else if (status === 403 && data) {
                    const left = data.remainingAttempts;
                    showError(error, `${data.error} ${left} attempt${left === 1 ? "" : "s"} left before the page is deleted.`);
                    pass.select();
                } else if (status === 404 || status === 410) {
                    showGone(data?.error);
                } else {
                    showError(error, errorMessage(status, data));
                }
            } catch {
                showError(error, "Decryption failed. The page may be damaged.");
            } finally {
                btn.disabled = false;
                btn.textContent = "Open";
            }
        });
    }
})();
