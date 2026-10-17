// Fluent's SortableList discards the { stop } handle returned by Initialize, and its .NET callback
// reference is only disposed through a module path it never initializes. Blazor would keep that handle,
// and the list element it closes over, for the page's lifetime, while SortableJS keeps every list element
// in a module-level array. Give Blazor a handle without the element, and stop each list and release its
// callback reference once enhanced navigation or a re-render removes it.
const lists = new WeakMap();
let live = 0;
const observer = new MutationObserver(records => {
    for (const record of records) {
        for (const node of record.removedNodes) {
            if (!(node instanceof Element)) continue;
            for (const list of within(node, '.fluent-sortable-list')) {
                if (!list.isConnected) stop(list);
            }
        }
        if (record.target instanceof Element && lists.has(record.target)) assignItemRoles(record.target);
    }
});

function within(node, selector) {
    return node.matches(selector) ? [node] : node.querySelectorAll(selector);
}

// Fluent renders focusable item wrappers that Razor cannot annotate, so a list that takes part in an
// outer ARIA structure names their role with data-item-role.
function assignItemRoles(list) {
    const role = list.dataset.itemRole;
    if (!role) return;
    for (const item of list.children) {
        if (item.classList.contains('sortable-item') && item.getAttribute('role') !== role) item.setAttribute('role', role);
    }
}

function stop(list) {
    const entry = lists.get(list);
    if (!entry) return;
    lists.delete(list);
    entry.handle.stop();
    entry.reference.dispose();
    if (--live === 0) observer.disconnect();
}

// Fluent moves a keyboard cross-list item into the destination before its add completes, then puts it
// back in the source while awaiting the remove. Remember the moved category and an interaction token so
// completion focuses that category wherever it actually landed, and never overrides a newer interaction.
// The add callback runs after Fluent's GetItemJS round trip, so a time window cannot identify the
// initiating key press; keep the token until the add consumes it.
let keyboardMove = null;
let interaction = 0;

function releaseInteraction() {
    interaction++;
    keyboardMove = null;
}

document.addEventListener('keydown', event => {
    interaction++;
    const item = document.activeElement;
    if ((event.key === 'ArrowLeft' || event.key === 'ArrowRight')
        && item instanceof Element && item.classList.contains('sortable-item') && item.getAttribute('aria-grabbed') === 'true') {
        const row = item.querySelector('[data-category-id]');
        keyboardMove = { itemId: row?.getAttribute('data-category-id') ?? null, generation: interaction };
        return;
    }
    keyboardMove = null;
}, true);
document.addEventListener('pointerdown', releaseInteraction, true);
document.addEventListener('enhancednavigationstart', releaseInteraction, true);
window.addEventListener('popstate', releaseInteraction, true);
window.addEventListener('hashchange', releaseInteraction, true);

function wrapperFor(listId, itemId) {
    const rows = document.getElementById(listId)?.querySelectorAll('[data-category-id]') ?? [];
    for (const row of rows) {
        if (row.getAttribute('data-category-id') === itemId) return row.closest('.sortable-item');
    }
    return null;
}

// A rejected save resolves the add normally and rolls the order back, so the category can be in the
// source list again; locate it by id instead of trusting the destination index. Fluent focuses that
// index 50 ms after the add returns, so restore afterwards and abandon the work when focus or state moved on.
async function restoreKeyboardFocus(move, toListId, fromListId) {
    if (!move.itemId) return;
    await new Promise(resolve => setTimeout(resolve, 60));
    const deadline = performance.now() + 1000;
    do {
        if (move.generation !== interaction) return;
        const item = wrapperFor(toListId, move.itemId) ?? wrapperFor(fromListId, move.itemId);
        if (item) {
            item.focus();
            if (document.activeElement === item) {
                for (const grabbed of document.querySelectorAll('.sortable-item[aria-grabbed="true"]')) {
                    if (grabbed !== item) grabbed.setAttribute('aria-grabbed', 'false');
                }
                item.setAttribute('aria-grabbed', 'true');
                await new Promise(requestAnimationFrame);
                if (move.generation !== interaction) return;
                if (item.isConnected && document.activeElement === item) return;
            }
        }
        await new Promise(requestAnimationFrame);
    } while (performance.now() < deadline);
}

// The parent component owns the order and re-renders the source list as soon as the destination accepts
// a cross-list move. Fluent then applies the source's removal to the already updated item list, so it is
// not forwarded; Fluent restores the source DOM in its remove handler either way.
// Fluent's keyboard move awaits the destination's add before that restore runs, while its add handler has
// already detached the item. Blazor's render of the move would then miss the node and abandon the batch,
// so the add is started only after the remove handler has put the source DOM back.
function callbacks(reference) {
    return {
        invokeMethodAsync: (name, ...args) => {
            if (name === 'OnRemoveJSAsync') return Promise.resolve();
            if (name !== 'OnAddJSAsync') return reference.invokeMethodAsync(name, ...args);
            const move = keyboardMove;
            keyboardMove = null;
            setTimeout(() => {
                reference.invokeMethodAsync(name, ...args).then(
                    () => { if (move) void restoreKeyboardFocus(move, args[3], args[2]); },
                    () => { /* Blazor reports a failed add through the renderer. */ });
            });
            return Promise.resolve();
        },
    };
}

export function initializeSortableLists() {
    const sortable = window.Microsoft?.FluentUI?.Blazor?.Components?.SortableList;
    if (!sortable?.Initialize || sortable.Initialize.releasesRemovedLists) return;
    const initialize = sortable.Initialize;
    sortable.Initialize = async (list, ...options) => {
        const reference = options.pop();
        const handle = await initialize(list, ...options, callbacks(reference));
        lists.set(list, { handle, reference });
        if (live++ === 0) observer.observe(document.documentElement, { childList: true, subtree: true });
        assignItemRoles(list);
        if (!list.isConnected) stop(list);
        return {};
    };
    sortable.Initialize.releasesRemovedLists = true;
}
