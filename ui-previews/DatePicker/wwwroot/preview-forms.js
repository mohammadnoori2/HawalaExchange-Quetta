window.previewForms = {
    open(id) { const dialog=document.getElementById(id); if(dialog && !dialog.open)dialog.showModal(); },
    close(id) { document.getElementById(id)?.close(); },
    focusInvalid(id) { requestAnimationFrame(()=>document.getElementById(id)?.querySelector('.invalid, [aria-required="true"]')?.focus()); }
};
