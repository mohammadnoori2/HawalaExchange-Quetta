(() => {
    const closeMobileMenu = () => {
        const toggle = document.getElementById("mobile-nav-toggle");
        if (toggle) toggle.checked = false;
    };

    document.addEventListener("click", event => {
        if (event.target.closest(".nav-sidebar a[href]")) closeMobileMenu();
    });

    document.addEventListener("keydown", event => {
        if (event.key === "Escape") closeMobileMenu();
    });
})();
