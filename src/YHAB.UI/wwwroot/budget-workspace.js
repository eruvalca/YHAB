let handler;
let pointerInput;

function amountInput(event) {
    const path = event.composedPath();
    if (!path.some(element => element instanceof Element && element.matches('.amount-input'))) return;
    return path.find(element => element instanceof HTMLInputElement);
}

function selectAmount(event) {
    amountInput(event)?.select();
    // A sticky summary must not cover controls brought into view by Tab or
    // an editor's heading focus. This also handles Fluent's shadow inputs.
    const path = event.composedPath();
    const board = path.find(element => element instanceof Element && element.matches('.budget-board'));
    const summary = board?.querySelector('.budget-summary');
    if (!summary || path.includes(summary)) return;
    const target = path.find(element => element instanceof HTMLElement);
    const box = target?.getBoundingClientRect();
    const header = summary.getBoundingClientRect();
    if (box && header.top >= 0 && box.top < header.bottom + 12) {
        window.scrollBy({ top: box.top - header.bottom - 12, behavior: 'instant' });
    }
}

function rememberPointerFocus(event) {
    const input = amountInput(event);
    // The first click selects everything; subsequent clicks can position the
    // caret to edit an expression. Keyboard focus selects synchronously too.
    pointerInput = input && input.getRootNode().activeElement !== input ? input : undefined;
}

function finishPointerFocus(event) {
    if (pointerInput && pointerInput === amountInput(event)) pointerInput.select();
    pointerInput = undefined;
}

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
    document.addEventListener('focusin', selectAmount);
    document.addEventListener('pointerdown', rememberPointerFocus);
    document.addEventListener('pointerup', finishPointerFocus);
}
export function disconnect() {
    if (handler) document.removeEventListener('keydown', handler);
    handler = undefined;
    document.removeEventListener('focusin', selectAmount);
    document.removeEventListener('pointerdown', rememberPointerFocus);
    document.removeEventListener('pointerup', finishPointerFocus);
    pointerInput = undefined;
}
