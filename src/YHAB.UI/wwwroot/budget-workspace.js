let handler;
export function connect(reference) {
    disconnect();
    handler = event => {
        if (event.defaultPrevented || event.composedPath().some(element =>
            element instanceof Element && element.matches('input, textarea, select, [contenteditable="true"]'))) return;
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') {
            event.preventDefault();
            reference.invokeMethodAsync('HistoryShortcutAsync', event.shiftKey);
        }
    };
    document.addEventListener('keydown', handler);
}
export function disconnect() {
    if (handler) document.removeEventListener('keydown', handler);
    handler = undefined;
}

