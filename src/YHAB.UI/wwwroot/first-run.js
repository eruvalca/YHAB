const dismissedPrefix = 'yhab.first-run.dismissed.';
const assignedPrefix = 'yhab.first-run.assigned.';

export function isDismissed(planId) {
    try {
        return localStorage.getItem(dismissedPrefix + planId) === 'true';
    } catch {
        // Private browsing can block local storage; the checklist still works for this page.
        return false;
    }
}

export function dismiss(planId) {
    try {
        localStorage.setItem(dismissedPrefix + planId, 'true');
    } catch {
        // The checklist stays dismissed for this render even without persisted storage.
    }
}

export function hasAssigned(planId) {
    try {
        return localStorage.getItem(assignedPrefix + planId) === 'true';
    } catch {
        return false;
    }
}

export function completeAssignment(planId) {
    try {
        localStorage.setItem(assignedPrefix + planId, 'true');
    } catch {
        // In-memory completion still hides the step for this render.
    }
}
