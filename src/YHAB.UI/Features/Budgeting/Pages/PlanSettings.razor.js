export function initializeShareCodeCopy() {
    // Delegation survives enhanced form posts without starting a .NET renderer.
    document.addEventListener('click', async event => {
        const button = event.target.closest('[data-copy-share-code]');
        if (!button || button.disabled) return;

        const notice = button.closest('[data-share-code]');
        const code = notice.querySelector('.share-code').textContent.trim();
        const status = notice.querySelector('[data-copy-share-code-status]');
        status.textContent = '';
        button.disabled = true;
        try {
            await navigator.clipboard.writeText(code);
            status.textContent = 'Share code copied.';
        } catch {
            status.textContent = 'Could not copy the code. Select the code and copy it manually.';
        } finally {
            button.disabled = false;
        }
    });
}
