window.pimConfirmDialog = window.pimConfirmDialog || {
    show(elementId, dotNetRef) {
        const element = document.getElementById(elementId);
        if (!element) return;
        // Move the modal to be a direct child of <body> so it always shares a stacking context with
        // Bootstrap's own backdrop (which it always appends to <body>). Without this, a modal declared
        // inside any positioned/z-indexed ancestor (e.g. a sticky header) can end up visually trapped
        // behind its own backdrop - it renders, but the backdrop intercepts every click, including the
        // close button's, so it can only be dismissed by a full page reload. Blazor Server keeps tracking
        // the element by reference after this move, so future re-renders still patch it correctly.
        if (element.parentElement !== document.body) {
            document.body.appendChild(element);
        }
        const modal = bootstrap.Modal.getOrCreateInstance(element);
        if (dotNetRef) {
            const handler = () => {
                element.removeEventListener('hidden.bs.modal', handler);
                dotNetRef.invokeMethodAsync('NotifyClosedAsync');
            };
            element.addEventListener('hidden.bs.modal', handler);
        }
        modal.show();
    },
    hide(elementId) {
        const element = document.getElementById(elementId);
        if (!element) return;
        bootstrap.Modal.getOrCreateInstance(element).hide();
    }
};
