const instances = new WeakMap();
let live = 0;
// Blazor can detach the table before disposal clears its element reference, so
// release the listeners and their remembered elements when the DOM removes it.
const removalObserver = new MutationObserver(records => {
    for (const record of records) {
        for (const node of record.removedNodes) {
            if (!(node instanceof Element)) continue;
            const tables = node.matches('.budget-table') ? [node] : node.querySelectorAll('.budget-table');
            for (const table of tables) {
                if (!table.isConnected) dispose(table);
            }
        }
    }
});

function rows(table) {
    return [...table.querySelectorAll('.category-row[data-category-id]')].filter(row => row.getClientRects().length > 0);
}

function nameControl(row) {
    return row.querySelector('.category-name .category-link');
}

function amountControl(row) {
    return row.querySelector('.assigned-cell fluent-text-input');
}

function controls(table, kind) {
    const select = kind === 'name' ? nameControl : amountControl;
    return rows(table).map(row => ({ row, control: select(row) })).filter(item => item.control && !item.control.disabled);
}

function synchronize(state) {
    for (const kind of ['name', 'amount']) {
        const items = controls(state.table, kind);
        const activeKey = kind === 'name' ? 'activeName' : 'activeAmount';
        const activeIdKey = kind === 'name' ? 'activeNameId' : 'activeAmountId';
        if (items.length === 0) continue;
        const remembered = items.find(item => item.row.dataset.categoryId === state[activeIdKey]);
        const active = remembered ?? items.find(item => item.control === state[activeKey]) ?? items[0];
        state[activeKey] = active.control;
        if (remembered || !state[activeIdKey]) state[activeIdKey] = active.row.dataset.categoryId;
        for (const item of items) item.control.tabIndex = item.control === active.control ? 0 : -1;
    }
}

function focus(state, row, kind) {
    const control = kind === 'name' ? nameControl(row) : amountControl(row);
    if (!control || control.disabled) return;
    if (kind === 'name') {
        state.activeName = control;
        state.activeNameId = row.dataset.categoryId;
    } else {
        state.activeAmount = control;
        state.activeAmountId = row.dataset.categoryId;
    }
    state.focused = { id: row.dataset.categoryId, kind };
    synchronize(state);
    control.focus();
}

function move(state, row, kind, delta) {
    const items = controls(state.table, kind);
    const index = items.findIndex(item => item.row === row);
    const target = items[index + delta];
    if (target) focus(state, target.row, kind);
}

function keydown(state, event) {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const row = event.target.closest?.('.category-row[data-category-id]');
    if (!row) return;
    const kind = event.target.matches('.category-name .category-link') ? 'name'
        : event.target.matches('.assigned-cell fluent-text-input') ? 'amount' : null;
    if (!kind) return;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        move(state, row, kind, event.key === 'ArrowDown' ? 1 : -1);
    } else if (event.key === 'ArrowRight' && kind === 'name') {
        event.preventDefault();
        focus(state, row, 'amount');
    } else if (event.key === 'ArrowLeft' && kind === 'amount') {
        const input = event.target.shadowRoot?.activeElement;
        if (!input || input.selectionStart !== 0 || input.selectionEnd !== 0) return;
        event.preventDefault();
        focus(state, row, 'name');
    }
}

function focusin(state, event) {
    const row = event.target.closest?.('.category-row[data-category-id]');
    if (!row) {
        state.focused = null;
        synchronize(state);
        return;
    }
    if (event.target.matches('.category-name .category-link')) {
        state.activeName = event.target;
        state.activeNameId = row.dataset.categoryId;
        state.focused = { id: row.dataset.categoryId, kind: 'name' };
    } else if (event.target.matches('.assigned-cell fluent-text-input')) {
        state.activeAmount = event.target;
        state.activeAmountId = row.dataset.categoryId;
        state.focused = { id: row.dataset.categoryId, kind: 'amount' };
    } else if (event.target.matches('.category-name .rename-name') || event.target.closest('.category-name .inline-name-form')) {
        const name = nameControl(row);
        if (name) state.activeName = name;
        state.activeNameId = row.dataset.categoryId;
        state.focused = { id: row.dataset.categoryId, kind: 'name' };
    } else {
        state.focused = null;
    }
    synchronize(state);
}

function restore(state) {
    synchronize(state);
    const active = document.activeElement;
    if (!state.focused || (active && active !== document.body && active.isConnected)) return;
    const row = rows(state.table).find(item => item.dataset.categoryId === state.focused.id);
    if (row) focus(state, row, state.focused.kind);
}

export function initialize(table) {
    if (instances.has(table)) return;
    const controller = new AbortController();
    const state = { table, controller, activeName: null, activeNameId: null, activeAmount: null, activeAmountId: null, focused: null };
    const observer = new MutationObserver(() => restore(state));
    state.observer = observer;
    instances.set(table, state);
    table.addEventListener('keydown', event => keydown(state, event), { signal: controller.signal });
    table.addEventListener('focusin', event => focusin(state, event), { signal: controller.signal });
    table.addEventListener('focusout', event => {
        if (!event.target.matches('.category-name .category-link, .assigned-cell fluent-text-input')) return;
        queueMicrotask(() => {
            if (!event.target.disabled && event.target.isConnected && !table.contains(document.activeElement)) state.focused = null;
        });
    }, { signal: controller.signal });
    document.addEventListener('focusin', event => {
        if (!table.contains(event.target)) state.focused = null;
    }, { signal: controller.signal });
    document.addEventListener('pointerdown', event => {
        if (!table.contains(event.target)) state.focused = null;
    }, { signal: controller.signal });
    observer.observe(table, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'] });
    if (++live === 1) removalObserver.observe(document.documentElement, { childList: true, subtree: true });
    if (!table.isConnected) dispose(table);
    else synchronize(state);
}

export function dispose(table) {
    const state = instances.get(table);
    if (!state) return;
    state.controller.abort();
    state.observer.disconnect();
    instances.delete(table);
    if (--live === 0) removalObserver.disconnect();
}
