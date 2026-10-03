export function initializeNavigation(blazor) {
    const updateThemeSelector = () => {
        for (const select of document.querySelectorAll('[data-theme-select]')) {
            select.value = blazor.theme?.getThemeSettings()?.mode ?? 'system';
        }
    };
    document.addEventListener('change', (event) => {
        if (event.target.matches('[data-theme-select]')) {
            blazor.theme.setThemeMode(event.target.value);
            updateThemeSelector();
        }
    });
    blazor.addEventListener('enhancedload', updateThemeSelector);
    updateThemeSelector();
    // Static SSR doesn't execute FluentNavItem's interactive drawer-close handler.
    // Close the web component before Blazor patches the next page into the document.
    blazor.addEventListener('enhancednavigationstart', () => {
        for (const drawer of document.querySelectorAll('.app-layout fluent-drawer[hamburger]')) {
            if (typeof drawer.hide === 'function') {
                drawer.hide();
            }
        }
    });
}
