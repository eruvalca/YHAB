export function initializeNavigation(blazor) {
    const navigationKey = 'yhab.navigation.collapsed';
    let navigationCollapsed = false;
    try {
        navigationCollapsed = localStorage.getItem(navigationKey) === 'true';
    } catch {
        // Navigation still works when the browser disallows local storage.
    }
    const updateNavigation = () => {
        const navigation = document.getElementById('desktop-navigation');
        if (navigation) navigation.dataset.collapsed = String(navigationCollapsed);
        for (const toggle of document.querySelectorAll('[data-navigation-toggle]')) {
            toggle.setAttribute('aria-expanded', String(!navigationCollapsed));
            toggle.querySelector('[data-navigation-label]').textContent = navigationCollapsed ? 'Show menu' : 'Hide menu';
        }
    };
    document.addEventListener('click', event => {
        if (!event.target.closest('[data-navigation-toggle]')) return;
        navigationCollapsed = !navigationCollapsed;
        updateNavigation();
        try {
            localStorage.setItem(navigationKey, String(navigationCollapsed));
        } catch {
            // Keep the current page usable without a persisted preference.
        }
    });
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
    blazor.addEventListener('enhancedload', () => {
        // Enhanced navigation restores the server's body attributes. Reapply
        // Fluent's resolved theme so native controls and dark-mode CSS agree.
        blazor.theme?.setThemeMode(blazor.theme.getThemeSettings()?.mode ?? 'system');
        updateThemeSelector();
        updateNavigation();
    });
    updateThemeSelector();
    updateNavigation();
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
