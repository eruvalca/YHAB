let handler;
let pointerInput;

function startCatalogDrag(event) {
    const handle = event.target.closest('.budget-table .row-move[draggable="true"]');
    if (!handle || !event.dataTransfer) return;
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', handle.getAttribute('aria-label'));
}

function submitInlineName(event) {
    if (event.key !== 'Enter') return;
    const path = event.composedPath();
    const form = path.find(element => element instanceof HTMLFormElement && element.matches('.inline-name-form'));
    const input = path.find(element => element instanceof HTMLInputElement);
    if (!form || !input) return;
    // Fluent's native input lives in a shadow root. Commit its change before
    // submitting, including paste/autofill that hasn't produced a keyup event.
    event.preventDefault();
    input.blur();
    form.requestSubmit();
}

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
    document.addEventListener('dragstart', startCatalogDrag);
    document.addEventListener('keydown', submitInlineName, true);
}
export function disconnect() {
    if (handler) document.removeEventListener('keydown', handler);
    handler = undefined;
    document.removeEventListener('focusin', selectAmount);
    document.removeEventListener('pointerdown', rememberPointerFocus);
    document.removeEventListener('pointerup', finishPointerFocus);
    document.removeEventListener('dragstart', startCatalogDrag);
    document.removeEventListener('keydown', submitInlineName, true);
    pointerInput = undefined;
}
