// Compatibility fix for the FAST definition observers bundled in Fluent UI 5.0.0.
// Definition changes still reconnect live controls, but must not retain removed DOM.
const yhabDefinitionObservers = new WeakMap();
const yhabDefinitionFinalizer = new FinalizationRegistry(releaseYhabDefinitionObserver);

function releaseYhabDefinitionObserver({ notifier, subscriber }) {
    notifier.unsubscribe(subscriber, 'template');
    notifier.unsubscribe(subscriber, 'shadowOptions');
}

function createYhabDefinitionObserver(controllerType, elementReference, notifier) {
    const subscriber = {
        handleChange() {
            const element = elementReference.deref();
            if (element) {
                controllerType.forCustomElement(element, true);
                element.$fastController.connect();
            } else {
                releaseYhabDefinitionObserver({ notifier, subscriber });
            }
        }
    };
    return subscriber;
}

function observeYhabDefinition(controllerType, element, definition, observable) {
    if (yhabDefinitionObservers.has(element)) return;
    const notifier = observable.getNotifier(definition);
    // Construct the callback in its own scope so it captures only the WeakRef.
    const subscriber = createYhabDefinitionObserver(controllerType, new WeakRef(element), notifier);
    const registration = { notifier, subscriber };
    notifier.subscribe(subscriber, 'template');
    notifier.subscribe(subscriber, 'shadowOptions');
    yhabDefinitionObservers.set(element, registration);
    yhabDefinitionFinalizer.register(element, registration);
}
