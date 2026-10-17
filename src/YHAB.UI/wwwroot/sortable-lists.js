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
// back in the source while awaiting the remove. Remember a real arrow-key move so the add can restore
// focus after the destination renders instead of relying on Fluent's fixed 50 ms focus timer.
// The add callback runs after Fluent's GetItemJS round trip, so a time window cannot identify the
// initiating key press; keep the flag until the add consumes it or another interaction starts.
let keyboardMove = false;
document.addEventListener('keydown', event => {
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
        const item = document.activeElement;
        keyboardMove = item instanceof Element && item.classList.contains('sortable-item') && item.getAttribute('aria-grabbed') === 'true';
    }
    else if (event.key === 'Tab' || event.key === 'Escape' || event.key === 'Enter' || event.key === ' ') {
        keyboardMove = false;
    }
}, true);
document.addEventListener('pointerdown', () => { keyboardMove = false; }, true);

async function focusAfterAdd(toListId, index, fromListId, oldIndex) {
    const deadline = performance.now() + 1000;
    do {
        const item = document.getElementById(toListId)?.children[index];
        if (item) {
            item.focus();
            if (document.activeElement === item) {
                item.setAttribute('aria-grabbed', 'true');
                document.getElementById(fromListId)?.children[oldIndex]?.setAttribute('aria-grabbed', 'false');
                await new Promise(requestAnimationFrame);
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
            const keyboard = keyboardMove;
            keyboardMove = false;
            setTimeout(() => {
                reference.invokeMethodAsync(name, ...args).then(
                    () => { if (keyboard) void focusAfterAdd(args[3], args[1], args[2], args[0]); },
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
