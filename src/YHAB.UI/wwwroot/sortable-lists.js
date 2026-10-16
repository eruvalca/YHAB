// Fluent's SortableList discards the { stop } handle returned by Initialize. Blazor would keep that
// handle, and the list element it closes over, for the page's lifetime, while SortableJS keeps every
// list element in a module-level array. Give Blazor a handle without the element, and stop each list
// once enhanced navigation or a re-render removes it.
const handles = new WeakMap();
let live = 0;
const observer = new MutationObserver(records => {
    for (const record of records) {
        for (const node of record.removedNodes) {
            if (!(node instanceof Element)) continue;
            for (const list of node.matches('.fluent-sortable-list') ? [node] : node.querySelectorAll('.fluent-sortable-list')) {
                if (!list.isConnected) stop(list);
            }
        }
    }
});

function stop(list) {
    const handle = handles.get(list);
    if (!handle) return;
    handles.delete(list);
    handle.stop();
    if (--live === 0) observer.disconnect();
}

export function initializeSortableLists() {
    const lists = window.Microsoft?.FluentUI?.Blazor?.Components?.SortableList;
    if (!lists?.Initialize || lists.Initialize.releasesRemovedLists) return;
    const initialize = lists.Initialize;
    lists.Initialize = async (list, ...options) => {
        const handle = await initialize(list, ...options);
        handles.set(list, handle);
        if (live++ === 0) observer.observe(document.documentElement, { childList: true, subtree: true });
        if (!list.isConnected) stop(list);
        return {};
    };
    lists.Initialize.releasesRemovedLists = true;
}
