// Fluent renders `inputmode` on the host element of its input components, but the
// matching web component keeps the native control in a shadow root and never
// forwards the attribute to it. Without the mirror a phone shows the full
// keyboard for fields that asked for a numeric one, so copy the attribute onto
// the control instead of replacing the Fluent components.
const hostSelector = 'fluent-text-input[inputmode], fluent-text-area[inputmode]';
const retries = new WeakMap();
let observer;

function mirrorInputMode(host) {
    const mode = host.getAttribute('inputmode');
    if (!mode) return;
    const control = host.shadowRoot?.querySelector('input, textarea');
    if (!control) {
        // Fluent defines its custom elements lazily, so the shadow control can
        // appear after the host. Retry briefly while the element upgrades.
        const attempt = (retries.get(host) ?? 0) + 1;
        if (attempt <= 60) {
            retries.set(host, attempt);
            requestAnimationFrame(() => mirrorInputMode(host));
        }
        return;
    }
    retries.delete(host);
    if (control.getAttribute('inputmode') !== mode) control.setAttribute('inputmode', mode);
}

function mirrorAll(root) {
    if (root.matches?.(hostSelector)) mirrorInputMode(root);
    for (const host of root.querySelectorAll?.(hostSelector) ?? []) mirrorInputMode(host);
}

export function initializeInputMode() {
    if (observer) return;
    observer = new MutationObserver(records => {
        for (const record of records) {
            if (record.type === 'attributes') {
                mirrorInputMode(record.target);
                continue;
            }
            for (const node of record.addedNodes) {
                if (node.nodeType === Node.ELEMENT_NODE) mirrorAll(node);
            }
        }
    });
    observer.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['inputmode'] });
    // Hosts rendered before Fluent defined the element are upgraded on definition.
    for (const tag of ['fluent-text-input', 'fluent-text-area']) {
        customElements.whenDefined(tag).then(() => mirrorAll(document.body), () => { });
    }
    mirrorAll(document.body);
}
