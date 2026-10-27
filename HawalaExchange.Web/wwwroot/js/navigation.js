(() => {
    const mobile = window.matchMedia("(max-width: 1024px)");
    let currentToggle;
    let previousFocus;

    const synchronizeMenu = () => {
        const toggle = document.getElementById("mobile-nav-toggle");
        const sidebar = document.getElementById("app-sidebar");
        const main = document.querySelector(".app-main");
        if (!toggle || !sidebar || !main) return;

        const open = mobile.matches && toggle.checked;
        toggle.setAttribute("aria-expanded", String(open));
        sidebar.inert = mobile.matches && !open;
        main.inert = open;
        document.body.classList.toggle("mobile-nav-open", open);
    };

    const closeMobileMenu = () => {
        const toggle = document.getElementById("mobile-nav-toggle");
        const wasOpen = toggle?.checked;
        if (toggle) toggle.checked = false;
        synchronizeMenu();
        if (wasOpen && previousFocus?.isConnected) previousFocus.focus();
    };

    const bindMenu = () => {
        const toggle = document.getElementById("mobile-nav-toggle");
        if (toggle === currentToggle) return;
        currentToggle = toggle;
        document.body.classList.remove("mobile-nav-open");
        if (!toggle) return;
        toggle.addEventListener("change", () => {
            if (toggle.checked) previousFocus = toggle;
            synchronizeMenu();
        });
        synchronizeMenu();
    };

    document.addEventListener("click", event => {
        if (event.target.closest(".nav-sidebar a[href]")) closeMobileMenu();
    });

    document.addEventListener("keydown", event => {
        if (!mobile.matches || !currentToggle?.checked) return;
        if (event.key === "Escape") {
            closeMobileMenu();
            event.preventDefault();
        }
        if (event.key === "Tab") {
            const elements = [currentToggle, ...document.querySelectorAll(
                "#app-sidebar a[href], #app-sidebar button:not([disabled]), #app-sidebar summary, #app-sidebar input:not([disabled])"
            )].filter(element => element.getClientRects().length && getComputedStyle(element).visibility !== "hidden");
            const first = elements[0];
            const last = elements.at(-1);
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last?.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first?.focus();
            }
        }
    });

    mobile.addEventListener("change", () => {
        if (!mobile.matches) closeMobileMenu();
        synchronizeMenu();
    });
    // Interactive routing may mount the shell after this script first executes.
    new MutationObserver(bindMenu).observe(document.body, { childList: true, subtree: true });
    bindMenu();
})();
