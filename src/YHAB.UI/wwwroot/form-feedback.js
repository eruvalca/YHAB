export function initializeFormFeedback(blazor) {
    blazor.addEventListener('enhancedload', () => {
        const invalidControl = document.querySelector('[data-validation-focus].invalid, [data-validation-focus][aria-invalid="true"]');
        if (!(invalidControl instanceof HTMLElement)) {
            return;
        }

        invalidControl.focus({ preventScroll: true });
        invalidControl.scrollIntoView({ block: 'center' });
    });
}
