// ===== فقط یک بار تعریف کنید =====
let activeHawalaOperationMenu = null;

function positionHawalaOperationMenu(button, menu) {
    const viewportGap = 8;
    const buttonGap = 6;

    menu.style.visibility = "hidden";
    menu.style.top = "0px";
    menu.style.left = "0px";

    const buttonRect = button.getBoundingClientRect();
    const menuRect = menu.getBoundingClientRect();

    let top = buttonRect.bottom + buttonGap;
    const topWhenOpenedUpward = buttonRect.top - menuRect.height - buttonGap;

    if (top + menuRect.height > window.innerHeight - viewportGap && topWhenOpenedUpward >= viewportGap) {
        top = topWhenOpenedUpward;
    }

    top = Math.max(
        viewportGap,
        Math.min(top, window.innerHeight - menuRect.height - viewportGap)
    );

    let left = buttonRect.right - menuRect.width;
    left = Math.max(
        viewportGap,
        Math.min(left, window.innerWidth - menuRect.width - viewportGap)
    );

    menu.style.top = `${Math.round(top)}px`;
    menu.style.left = `${Math.round(left)}px`;
    menu.style.visibility = "visible";
}

function cleanupHawalaOperationMenu(state) {
    document.removeEventListener("pointerdown", state.onPointerDown, true);
    document.removeEventListener("keydown", state.onKeyDown, true);
    window.removeEventListener("scroll", state.onScroll, true);
    window.removeEventListener("resize", state.onResize);
}

function closeHawalaOperationMenuFromBrowser(state) {
    cleanupHawalaOperationMenu(state);

    if (activeHawalaOperationMenu === state) {
        activeHawalaOperationMenu = null;
    }

    void state.dotNetReference.invokeMethodAsync("CloseFromJs").catch(() => { });
}

function getActionMenuStorageKey(preferenceKey) {
    return `hawala:action-menu:${preferenceKey}`;
}

function getActionMenuItems(menu) {
    if (!menu) {
        return [];
    }

    const usedKeys = new Map();
    return Array.from(menu.querySelectorAll(".operation-menu-items .operation-menu-item"))
        .filter(button => !button.classList.contains("operation-menu-configure-button"))
        .map(button => {
            const label = (button.textContent || "").replace(/\s+/g, " ").trim();
            const requestedKey = button.dataset.actionKey || label || "action";
            const duplicateNumber = usedKeys.get(requestedKey) || 0;
            usedKeys.set(requestedKey, duplicateNumber + 1);
            const key = duplicateNumber === 0 ? requestedKey : `${requestedKey}-${duplicateNumber + 1}`;
            const icon = button.querySelector("i");

            button.dataset.resolvedActionKey = key;
            return {
                key,
                label,
                iconClass: icon?.className || "bi bi-lightning",
                button,
                danger: button.classList.contains("operation-menu-danger")
            };
        });
}

function getSelectedActionKeys(preferenceKey, defaults) {
    const storedValue = localStorage.getItem(getActionMenuStorageKey(preferenceKey));
    if (storedValue !== null) {
        try {
            const parsed = JSON.parse(storedValue);
            if (Array.isArray(parsed)) {
                return new Set(parsed.map(String));
            }
        } catch {
            // A damaged preference is ignored and replaced with defaults.
        }
    }

    return new Set((defaults || []).map(String));
}

function tidyActionMenuDividers(menu) {
    const children = Array.from(menu.querySelector(".operation-menu-items")?.children || []);
    children.forEach((child, index) => {
        if (!child.classList.contains("operation-menu-divider")) {
            return;
        }

        const hasVisibleBefore = children.slice(0, index).some(item =>
            item.classList.contains("operation-menu-item") && !item.classList.contains("operation-menu-promoted"));
        const hasVisibleAfter = children.slice(index + 1).some(item =>
            item.classList.contains("operation-menu-item") && !item.classList.contains("operation-menu-promoted"));
        child.classList.toggle("operation-menu-divider-hidden", !hasVisibleBefore || !hasVisibleAfter);
    });
}

function syncOperationMenuQuickActions(menuId, quickHostId, preferenceKey, defaults) {
    const menu = document.getElementById(menuId);
    const quickHost = document.getElementById(quickHostId);
    if (!menu || !quickHost) {
        return;
    }

    const selectedKeys = getSelectedActionKeys(preferenceKey, defaults);
    const actions = getActionMenuItems(menu);
    quickHost.replaceChildren();

    actions.forEach(action => {
        const promoted = selectedKeys.has(action.key) || selectedKeys.has(action.label);
        action.button.classList.toggle("operation-menu-promoted", promoted);
        if (!promoted) {
            return;
        }

        const quickButton = document.createElement("button");
        quickButton.type = "button";
        quickButton.className = `operation-quick-action${action.danger ? " operation-quick-action-danger" : ""}`;
        quickButton.title = action.label;
        quickButton.setAttribute("aria-label", action.label);
        quickButton.disabled = action.button.disabled;

        const icon = action.button.querySelector("i");
        if (icon) {
            quickButton.appendChild(icon.cloneNode(true));
        } else {
            const fallbackIcon = document.createElement("i");
            fallbackIcon.className = "bi bi-lightning";
            quickButton.appendChild(fallbackIcon);
        }

        quickButton.addEventListener("click", event => {
            event.stopPropagation();
            if (!action.button.disabled) {
                action.button.click();
            }
        });
        quickHost.appendChild(quickButton);
    });

    tidyActionMenuDividers(menu);
}

window.hawalaTools = {
    openFloatingDropdown: function (trigger, menu) {
        if (!trigger || !menu) return;

        if (!menu.matches(":popover-open")) {
            menu.showPopover();
        }

        const gap = 4;
        const viewportGap = 10;
        const triggerRect = trigger.getBoundingClientRect();
        const width = Math.min(
            Math.max(triggerRect.width, 320),
            window.innerWidth - (viewportGap * 2));
        const left = Math.max(
            viewportGap,
            Math.min(triggerRect.right - width, window.innerWidth - width - viewportGap));
        const roomBelow = window.innerHeight - triggerRect.bottom - viewportGap;
        const roomAbove = triggerRect.top - viewportGap;
        const openUpward = roomBelow < 180 && roomAbove > roomBelow;
        const availableHeight = Math.max(120, Math.min(260, openUpward ? roomAbove - gap : roomBelow - gap));

        menu.style.position = "fixed";
        menu.style.right = "auto";
        menu.style.left = `${Math.round(left)}px`;
        menu.style.width = `${Math.round(width)}px`;
        menu.style.maxHeight = `${Math.round(availableHeight)}px`;
        menu.style.margin = "0";

        if (openUpward) {
            menu.style.top = "auto";
            menu.style.bottom = `${Math.round(window.innerHeight - triggerRect.top + gap)}px`;
        } else {
            menu.style.top = `${Math.round(triggerRect.bottom + gap)}px`;
            menu.style.bottom = "auto";
        }
    },

    closeFloatingDropdown: function (menu) {
        if (menu?.matches(":popover-open")) {
            menu.hidePopover();
        }
    },

    syncOperationMenuQuickActions: function (menuId, quickHostId, preferenceKey, defaults) {
        syncOperationMenuQuickActions(menuId, quickHostId, preferenceKey, defaults);
    },

    getOperationMenuOptions: function (menuId, preferenceKey, defaults) {
        const menu = document.getElementById(menuId);
        const selectedKeys = getSelectedActionKeys(preferenceKey, defaults);
        return getActionMenuItems(menu).map(action => ({
            key: action.key,
            label: action.label,
            iconClass: action.iconClass,
            selected: selectedKeys.has(action.key) || selectedKeys.has(action.label)
        }));
    },

    saveOperationMenuPreferences: function (preferenceKey, selectedKeys) {
        localStorage.setItem(
            getActionMenuStorageKey(preferenceKey),
            JSON.stringify(selectedKeys || []));

        document.querySelectorAll(`[data-action-preference-key="${CSS.escape(preferenceKey)}"]`)
            .forEach(root => syncOperationMenuQuickActions(
                root.dataset.actionMenuId,
                root.dataset.actionQuickHostId,
                preferenceKey,
                []));
    },

    openOperationMenu: async function (buttonId, menuId, dotNetReference) {
        if (activeHawalaOperationMenu && activeHawalaOperationMenu.menuId !== menuId) {
            const previousMenu = activeHawalaOperationMenu;
            cleanupHawalaOperationMenu(previousMenu);
            activeHawalaOperationMenu = null;

            try {
                await previousMenu.dotNetReference.invokeMethodAsync("CloseFromJs");
            } catch {
                // The previous component may already have been disposed.
            }
        }

        const button = document.getElementById(buttonId);
        const menu = document.getElementById(menuId);

        if (!button || !menu) {
            return;
        }

        if (activeHawalaOperationMenu?.menuId === menuId) {
            positionHawalaOperationMenu(button, menu);
            return;
        }

        const state = {
            button,
            menu,
            menuId,
            dotNetReference,
            onPointerDown: null,
            onKeyDown: null,
            onScroll: null,
            onResize: null
        };

        state.onPointerDown = function (event) {
            if (!menu.contains(event.target) && !button.contains(event.target)) {
                closeHawalaOperationMenuFromBrowser(state);
            }
        };
        state.onKeyDown = function (event) {
            if (event.key === "Escape") {
                closeHawalaOperationMenuFromBrowser(state);
                button.focus();
            }
        };
        state.onScroll = function () {
            closeHawalaOperationMenuFromBrowser(state);
        };
        state.onResize = function () {
            positionHawalaOperationMenu(button, menu);
        };

        activeHawalaOperationMenu = state;
        positionHawalaOperationMenu(button, menu);

        document.addEventListener("pointerdown", state.onPointerDown, true);
        document.addEventListener("keydown", state.onKeyDown, true);
        window.addEventListener("scroll", state.onScroll, true);
        window.addEventListener("resize", state.onResize);
    },

    // helper to download base64 data url
    downloadFromDataUrl: function (dataUrl, fileName) {
        try {
            const a = document.createElement('a');
            a.href = dataUrl;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            a.remove();
        } catch (e) {
            console.error('downloadFromDataUrl', e);
            alert('خطا در دانلود فایل.');
        }
    },

    closeOperationMenu: function (menuId) {
        if (!activeHawalaOperationMenu || activeHawalaOperationMenu.menuId !== menuId) {
            return;
        }

        cleanupHawalaOperationMenu(activeHawalaOperationMenu);
        activeHawalaOperationMenu = null;

        const menu = document.getElementById(menuId);
        if (menu) {
            menu.style.visibility = "hidden";
        }
    },

    copyToClipboard: async function (text) {
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
                return true;
            }

            const textArea = document.createElement("textarea");
            textArea.value = text;
            textArea.style.position = "fixed";
            textArea.style.left = "-9999px";
            textArea.style.top = "-9999px";

            document.body.appendChild(textArea);
            textArea.focus();
            textArea.select();

            const result = document.execCommand("copy");

            document.body.removeChild(textArea);

            return result;
        } catch {
            return false;
        }
    },

    openShareUrl: function (url) {
        window.open(url, "_blank");
    },

    openTelegramApp: function (url) {
        window.location.href = url;
    },

    printReceipt: function () {
        window.print();
    },

    printHtml: function (htmlContent) {
        var printWindow = window.open('', '_blank', 'width=600,height=800,scrollbars=yes');
        if (printWindow) {
            printWindow.document.write(htmlContent);
            printWindow.document.close();
        } else {
            alert('لطفاً باز کردن پنجره جدید را مجاز کنید');
        }
    },

    printPdfElement: function (elementId, documentTitle) {
        const element = document.getElementById(elementId);

        if (!element) {
            return false;
        }

        const printWindow = window.open("", "_blank", "width=1200,height=850,scrollbars=yes");

        if (!printWindow) {
            return false;
        }

        const clonedElement = element.cloneNode(true);
        const title = documentTitle || "hawala-range-report";

        printWindow.document.open();
        printWindow.document.write(`
<!DOCTYPE html>
<html lang="fa" dir="rtl">
<head>
    <meta charset="utf-8" />
    <title>${title}</title>
    <style>
        @page {
            size: A4 landscape;
            margin: 10mm;
        }

        * {
            box-sizing: border-box;
        }

        html,
        body {
            margin: 0;
            padding: 0;
            background: #ffffff;
            color: #172033;
            direction: rtl;
            font-family: Tahoma, Arial, sans-serif;
            font-size: 10px;
            -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
        }

        .range-report-header {
            padding-bottom: 10px;
            margin-bottom: 14px;
            border-bottom: 2px solid #3157a4;
        }

        .range-report-header h4 {
            margin: 0 0 10px;
            color: #24478f;
            font-size: 18px;
            text-align: center;
        }

        .row {
            display: grid;
            grid-template-columns: repeat(4, minmax(0, 1fr));
            gap: 8px;
        }

        .col-md-3 {
            padding: 7px 9px;
            border: 1px solid #d8dfeb;
            border-radius: 5px;
            background: #f6f8fc;
        }

        .table-responsive {
            overflow: visible !important;
        }

        table {
            width: 100%;
            border-collapse: collapse;
            table-layout: fixed;
            margin-bottom: 14px;
        }

        .hawala-range-table {
            font-size: 8.5px;
        }

        .hawala-range-table th,
        .hawala-range-table td {
            padding: 5px 3px;
        }

        thead {
            display: table-header-group;
        }

        tr {
            break-inside: avoid;
            page-break-inside: avoid;
        }

        th,
        td {
            border: 1px solid #cbd3df;
            padding: 6px 5px;
            text-align: center;
            vertical-align: middle;
            overflow-wrap: anywhere;
        }

        th {
            background: #3157a4 !important;
            color: #ffffff !important;
            font-weight: 700;
        }

        tbody tr:nth-child(even) td {
            background: #f5f7fb;
        }

        h6 {
            margin: 14px 0 8px;
            color: #24478f;
            font-size: 13px;
        }

        .fw-bold {
            font-weight: 700;
        }

        .text-danger {
            color: #b42318 !important;
        }

        .text-success {
            color: #067647 !important;
        }

        .text-primary {
            color: #24478f !important;
        }

        .text-muted {
            color: #667085 !important;
        }

        .mb-0,
        .mb-3,
        .mb-4 {
            margin-bottom: 0;
        }

        [dir="ltr"] {
            direction: ltr;
            unicode-bidi: embed;
        }
    </style>
</head>
<body>
    ${clonedElement.outerHTML}
</body>
</html>`);

        printWindow.onload = function () {
            setTimeout(function () {
                printWindow.focus();
                printWindow.print();
            }, 350);
        };

        printWindow.document.close();

        return true;
    },

    printElement: function (elementId, paperWidthMm) {
        const element = document.getElementById(elementId);

        if (!element) {
            alert("Receipt area not found.");
            return;
        }

        const width = paperWidthMm || 80;
        const contentWidth = width === 58 ? 48 : 72;

        const clonedElement = element.cloneNode(true);
        clonedElement.querySelectorAll("style").forEach(style => style.remove());

        const receiptHtml = clonedElement.innerHTML;

        const printWindow = window.open("", "_blank", "width=420,height=700");

        if (!printWindow) {
            alert("Print window was blocked. Please allow popups for this site.");
            return;
        }

        printWindow.document.open();

        printWindow.document.write(`
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>Hawala Receipt</title>

    <style>
        @page {
            size: ${width}mm auto;
            margin: 0;
        }

        * {
            box-sizing: border-box;
        }

        html,
        body {
            margin: 0;
            padding: 0;
            width: ${width}mm;
            background: #ffffff;
            color: #000000;
            font-family: Tahoma, Arial, sans-serif;
            direction: rtl;
            -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
        }

        body {
            padding: 2mm 0;
        }

        .print-container {
            width: ${contentWidth}mm;
            margin: 0 auto;
            background: #ffffff;
            color: #000000;
        }

        .hawala-receipt {
            width: 100% !important;
            max-width: 100% !important;
            padding: 3mm !important;
            margin: 0 !important;
            border: 1.5px solid #c99a10 !important;
            border-radius: 0 !important;
            box-shadow: none !important;
            font-size: 10.5px !important;
            line-height: 1.5 !important;
            color: #000000 !important;
            background: #ffffff !important;
        }

        .receipt-brand {
            display: grid !important;
            grid-template-columns: minmax(0, 1fr) 19mm !important;
            align-items: center !important;
            gap: 2mm !important;
            padding: 0 0 2mm !important;
        }

        .receipt-brand-copy {
            text-align: center !important;
        }

        .receipt-brand-copy h3 {
            margin: 0 0 1mm !important;
            font-size: 14px !important;
            font-weight: 800 !important;
            line-height: 1.5 !important;
        }

        .receipt-kind {
            font-size: 11px !important;
            font-weight: 700 !important;
        }

        .receipt-brand .receipt-logo {
            width: 18mm !important;
            height: 18mm !important;
            margin: 0 !important;
            object-fit: contain !important;
        }

        .receipt-heading {
            margin: 0 0 1.5mm !important;
            text-align: center !important;
            font-size: 13px !important;
            font-weight: 800 !important;
        }

        .receipt-table {
            width: 100% !important;
            border-collapse: collapse !important;
            table-layout: fixed !important;
        }

        .receipt-table th,
        .receipt-table td {
            border: 1px solid #444 !important;
            padding: 1mm 1.2mm !important;
            vertical-align: middle !important;
            overflow-wrap: anywhere !important;
            background: #fff !important;
            color: #000 !important;
        }

        .receipt-table th {
            width: 31% !important;
            text-align: right !important;
            font-weight: 800 !important;
            background: #faf8ef !important;
        }

        .receipt-table td {
            text-align: right !important;
        }

        .receipt-time {
            float: left !important;
        }

        .receipt-strong,
        .receipt-amount {
            font-size: 11px !important;
            font-weight: 800 !important;
        }

        .receipt-company-contact {
            padding: 2mm 1mm 0 !important;
            text-align: center !important;
            font-size: 9px !important;
        }

        .receipt-company-address {
            margin-bottom: 1mm !important;
            font-weight: 600 !important;
        }

        .receipt-company-phone {
            font-weight: 700 !important;
        }

        .execution-declaration {
            margin-top: 3mm !important;
            padding: 2.5mm !important;
            border: 1px solid #555 !important;
            text-align: justify !important;
            font-size: 10.5px !important;
            font-weight: 600 !important;
            line-height: 1.9 !important;
        }

        .execution-signature {
            min-height: 30mm !important;
            padding: 17mm 7mm 0 !important;
            text-align: center !important;
            font-size: 10px !important;
        }

        .execution-signature-line {
            width: 42mm !important;
            margin: 0 auto 1.5mm !important;
            border-top: 1px solid #333 !important;
        }

        .receipt-contact-list {
            display: flex !important;
            flex-wrap: wrap !important;
            justify-content: space-around !important;
            gap: 1mm 2mm !important;
            font-weight: 700 !important;
        }

        .receipt-custom-note {
            margin-top: 2mm !important;
            padding-top: 1.5mm !important;
            border-top: 1px dashed #777 !important;
            text-align: center !important;
            font-size: 9px !important;
        }

        .receipt-legal-note {
            margin-top: 2mm !important;
            padding: 2mm 1mm 0 !important;
            border-top: 1px solid #c99a10 !important;
            text-align: center !important;
            font-size: 9.5px !important;
            font-weight: 800 !important;
            line-height: 1.7 !important;
        }

        .receipt-header {
            display: block !important;
            text-align: center !important;
            margin-bottom: 4px !important;
        }

        .receipt-company {
            display: block !important;
            text-align: center !important;
        }

        .receipt-company h4 {
            font-size: 13px !important;
            font-weight: 700 !important;
            margin: 0 0 2px 0 !important;
            padding: 0 !important;
        }

        .receipt-logo {
            width: 38px !important;
            height: 38px !important;
            object-fit: contain !important;
            border: none !important;
            padding: 0 !important;
            margin: 0 auto 3px auto !important;
            display: block !important;
        }

        .receipt-muted {
            font-size: 9.5px !important;
            color: #000000 !important;
            margin: 0 !important;
            padding: 0 !important;
        }

        .receipt-title-box {
            border: 1px dashed #000000 !important;
            border-radius: 0 !important;
            padding: 3px !important;
            margin: 5px 0 !important;
            min-width: 0 !important;
            text-align: center !important;
        }

        .receipt-title-box h5 {
            font-size: 12px !important;
            margin: 0 0 3px 0 !important;
            padding: 0 !important;
            color: #000000 !important;
        }

        hr {
            border: none !important;
            border-top: 1px dashed #000000 !important;
            margin: 5px 0 !important;
        }

        .receipt-status-row {
            display: block !important;
            margin: 0 !important;
        }

        .receipt-status-row div {
            display: block !important;
            margin-bottom: 2px !important;
        }

        .receipt-section {
            margin-top: 6px !important;
        }

        .receipt-section h6 {
            font-size: 11px !important;
            font-weight: 700 !important;
            color: #000000 !important;
            border-top: 1px dashed #000000 !important;
            border-bottom: 1px dashed #000000 !important;
            padding: 2px 0 !important;
            margin: 5px 0 4px 0 !important;
            text-align: center !important;
        }

        .receipt-grid {
            display: block !important;
        }

        .receipt-grid div,
        .receipt-full {
            display: block !important;
            margin-bottom: 2px !important;
            grid-column: auto !important;
        }

        .receipt-signatures {
            display: flex !important;
            justify-content: space-between !important;
            gap: 4mm !important;
            margin-top: 14mm !important;
            padding: 0 !important;
        }

        .receipt-signatures div {
            width: 50% !important;
            border-top: 1px solid #000000 !important;
            text-align: center !important;
            padding-top: 2px !important;
            font-size: 9px !important;
        }

        .receipt-footer {
            text-align: center !important;
            font-size: 9px !important;
            color: #000000 !important;
            margin-top: 8px !important;
            border-top: 1px dashed #000000 !important;
            padding-top: 4px !important;
        }
    </style>
</head>

<body>
    <div class="print-container">
        ${receiptHtml}
    </div>

    <script>
        function printNow() {
            window.focus();
            window.print();
        }

        window.onload = function () {
            const images = document.images;

            if (!images || images.length === 0) {
                setTimeout(printNow, 800);
                return;
            }

            let loaded = 0;

            function imageDone() {
                loaded++;

                if (loaded >= images.length) {
                    setTimeout(printNow, 800);
                }
            }

            for (let i = 0; i < images.length; i++) {
                if (images[i].complete) {
                    imageDone();
                } else {
                    images[i].onload = imageDone;
                    images[i].onerror = imageDone;
                }
            }
        };
    <\/script>
</body>
</html>
        `);

        printWindow.document.close();
    }
};
