const prefix = 'yhab.first-run.dismissed.';

function key(planId) {
    return prefix + planId;
}

export function isDismissed(planId) {
    try {
        return localStorage.getItem(key(planId)) === 'true';
    } catch {
        // Private browsing can block local storage; the checklist still works for this page.
        return false;
    }
}

export function dismiss(planId) {
    try {
        localStorage.setItem(key(planId), 'true');
    } catch {
        // The checklist stays dismissed for this render even without persisted storage.
    }
}
