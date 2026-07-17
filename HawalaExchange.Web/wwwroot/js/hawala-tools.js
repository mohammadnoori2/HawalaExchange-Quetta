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

    printReceipt: function () {
        window.print();
    }
};

window.hawalaTools = window.hawalaTools || {};

window.hawalaTools.openTelegramApp = function (url) {
    window.location.href = url;
};

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

    printReceipt: function () {
        window.print();
    },

    // ===== متد جدید برای چاپ HTML =====
    printHtml: function (htmlContent) {
        var printWindow = window.open('', '_blank', 'width=600,height=800,scrollbars=yes');
        if (printWindow) {
            printWindow.document.write(htmlContent);
            printWindow.document.close();
        } else {
            alert('لطفاً باز کردن پنجره جدید را مجاز کنید');
        }
    }
};

window.hawalaTools = window.hawalaTools || {};

window.hawalaTools.openTelegramApp = function (url) {
    window.location.href = url;
};