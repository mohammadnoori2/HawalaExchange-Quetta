window.hawalaTools = {
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

    printElement: function (elementId, paperWidthMm) {
        const element = document.getElementById(elementId);

        if (!element) {
            alert("Receipt area not found.");
            return;
        }

        const width = paperWidthMm || 80;
        const contentWidth = width === 58 ? 48 : 72;

        // Clone the receipt area
        const clonedElement = element.cloneNode(true);

        // Very important:
        // Remove component <style> tags from copied HTML.
        // Old @media print CSS can hide the receipt and create blank PDF.
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
            padding: 0 !important;
            margin: 0 !important;
            border: none !important;
            border-radius: 0 !important;
            box-shadow: none !important;
            font-size: 10.5px !important;
            line-height: 1.5 !important;
            color: #000000 !important;
            background: #ffffff !important;
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