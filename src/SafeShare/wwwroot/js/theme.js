// Light / Dark / Auto theme switch. Loaded synchronously in <head> so the saved theme is applied
// before the first paint (no flash of the wrong theme). The CSP forbids inline scripts, hence a file.
(function () {
    "use strict";

    var KEY = "mp-theme";
    var COLORS = { light: "#ffffff", dark: "#151d1f" };
    var root = document.documentElement;

    // Light is the default; "auto" (follow the operating system) is an explicit choice like the others.
    var DEFAULT = "light";

    function stored() {
        try {
            var value = localStorage.getItem(KEY);
            return value === "light" || value === "dark" || value === "auto" ? value : DEFAULT;
        } catch (e) {
            return DEFAULT;   // storage blocked (private mode, policy)
        }
    }

    function apply(choice) {
        if (choice === "auto") root.removeAttribute("data-theme");
        else root.setAttribute("data-theme", choice);

        // Browser UI colour (address bar on mobile). In auto mode each meta keeps matching its own media query.
        document.querySelectorAll('meta[name="theme-color"]').forEach(function (meta) {
            var own = (meta.getAttribute("media") || "").indexOf("dark") >= 0 ? "dark" : "light";
            meta.setAttribute("content", COLORS[choice === "auto" ? own : choice]);
        });

        document.querySelectorAll("[data-theme-choice]").forEach(function (button) {
            button.setAttribute("aria-pressed", String(button.getAttribute("data-theme-choice") === choice));
        });
    }

    function save(choice) {
        try {
            if (choice === DEFAULT) localStorage.removeItem(KEY);
            else localStorage.setItem(KEY, choice);
        } catch (e) { }
    }

    apply(stored());

    document.addEventListener("DOMContentLoaded", function () {
        apply(stored());
        document.querySelectorAll("[data-theme-choice]").forEach(function (button) {
            button.addEventListener("click", function () {
                var choice = button.getAttribute("data-theme-choice");
                save(choice);
                apply(choice);
            });
        });
    });

    // Keep other open tabs in sync.
    window.addEventListener("storage", function (e) {
        if (e.key === KEY || e.key === null) apply(stored());
    });
})();
