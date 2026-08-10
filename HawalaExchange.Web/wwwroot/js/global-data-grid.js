const gridStates = new WeakMap();

const ignoredHeaders = new Set([
    "",
    "#",
    "عملیات",
    "انتخاب",
    "جزئیات"
]);

export function initialize(table) {
    if (!table) {
        return;
    }

    let state = gridStates.get(table);
    if (!state) {
        state = { columnIndex: -1, direction: "none" };
        gridStates.set(table, state);
    }

    const headers = getHeaders(table);
    headers.forEach((header, columnIndex) => {
        if (!isSortable(header)) {
            header.dataset.sortable = "false";
            return;
        }

        header.classList.add("global-grid-sortable");
        header.tabIndex = header.tabIndex >= 0 ? header.tabIndex : 0;
        header.setAttribute("role", "button");
        header.setAttribute("aria-sort", "none");

        if (header.dataset.globalGridBound === "true") {
            return;
        }

        header.dataset.globalGridBound = "true";

        header.addEventListener("click", event => {
            if (event.target.closest("button, a, input, select, textarea")) {
                return;
            }

            sortTable(table, columnIndex);
        });

        header.addEventListener("keydown", event => {
            if (event.key !== "Enter" && event.key !== " ") {
                return;
            }

            event.preventDefault();
            sortTable(table, columnIndex);
        });
    });

    if (state.columnIndex >= 0 && state.direction !== "none") {
        applySort(table, state.columnIndex, state.direction);
    }
}

function getHeaders(table) {
    const headerRows = table.tHead?.rows;
    if (!headerRows?.length) {
        return [];
    }

    return Array.from(headerRows[headerRows.length - 1].cells);
}

function isSortable(header) {
    if (header.dataset.sortable === "false" ||
        header.classList.contains("sortable-header") ||
        header.colSpan > 1) {
        return false;
    }

    const title = normalizeText(header.textContent);
    return !ignoredHeaders.has(title);
}

function sortTable(table, columnIndex) {
    const state = gridStates.get(table);
    if (!state) {
        return;
    }

    const direction = state.columnIndex === columnIndex && state.direction === "ascending"
        ? "descending"
        : "ascending";

    state.columnIndex = columnIndex;
    state.direction = direction;

    applySort(table, columnIndex, direction);
}

function applySort(table, columnIndex, direction) {
    updateHeaders(table, columnIndex, direction);

    Array.from(table.tBodies).forEach(body => {
        const rows = Array.from(body.rows);
        const sortableRows = rows
            .map((row, originalIndex) => ({ row, originalIndex }))
            .filter(item => item.row.cells.length > columnIndex && item.row.cells[columnIndex].colSpan === 1);
        const fixedRows = rows.filter(row => row.cells.length <= columnIndex || row.cells[columnIndex].colSpan !== 1);

        sortableRows.sort((left, right) => {
            const leftValue = getCellValue(left.row.cells[columnIndex]);
            const rightValue = getCellValue(right.row.cells[columnIndex]);
            const comparison = compareValues(leftValue, rightValue);

            if (comparison === 0) {
                return left.originalIndex - right.originalIndex;
            }

            return direction === "ascending" ? comparison : -comparison;
        });

        const fragment = document.createDocumentFragment();
        sortableRows.forEach(item => fragment.appendChild(item.row));
        fixedRows.forEach(row => fragment.appendChild(row));
        body.appendChild(fragment);
    });
}

function updateHeaders(table, activeColumnIndex, direction) {
    getHeaders(table).forEach((header, columnIndex) => {
        const active = columnIndex === activeColumnIndex;
        header.setAttribute("aria-sort", active ? direction : "none");

        if (active) {
            header.dataset.sortDirection = direction;
        } else {
            delete header.dataset.sortDirection;
        }
    });
}

function getCellValue(cell) {
    return normalizeText(cell.dataset.sortValue ?? cell.textContent);
}

function compareValues(left, right) {
    if (!left && !right) return 0;
    if (!left) return 1;
    if (!right) return -1;

    const leftNumber = parseNumber(left);
    const rightNumber = parseNumber(right);
    if (leftNumber !== null && rightNumber !== null) {
        return leftNumber - rightNumber;
    }

    return left.localeCompare(right, "fa", {
        numeric: true,
        sensitivity: "base"
    });
}

function parseNumber(value) {
    const normalized = toLatinDigits(value)
        .replace(/[٬،,]/g, "")
        .replace(/\s+/g, "")
        .replace(/٪/g, "%");

    if (!/^[+-]?(?:\d+(?:\.\d+)?|\.\d+)%?$/.test(normalized)) {
        return null;
    }

    const number = Number.parseFloat(normalized.replace("%", ""));
    return Number.isFinite(number) ? number : null;
}

function normalizeText(value) {
    return toLatinDigits(value ?? "")
        .replace(/\u200c/g, " ")
        .replace(/\s+/g, " ")
        .trim();
}

function toLatinDigits(value) {
    return String(value)
        .replace(/[۰-۹]/g, digit => String("۰۱۲۳۴۵۶۷۸۹".indexOf(digit)))
        .replace(/[٠-٩]/g, digit => String("٠١٢٣٤٥٦٧٨٩".indexOf(digit)));
}

function initializeAll(root) {
    if (root instanceof Element && root.matches('table[data-global-grid="true"]')) {
        initialize(root);
    }

    root.querySelectorAll?.('table[data-global-grid="true"]').forEach(initialize);
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => initializeAll(document), { once: true });
} else {
    initializeAll(document);
}

const gridObserver = new MutationObserver(mutations => {
    mutations.forEach(mutation => {
        mutation.addedNodes.forEach(node => {
            if (node instanceof Element) {
                initializeAll(node);
            }
        });
    });
});

gridObserver.observe(document.documentElement, {
    childList: true,
    subtree: true
});
