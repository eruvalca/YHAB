export function initializeNavigation(blazor) {
    const navigationKey = 'yhab.navigation.collapsed';
    let navigationPreference = null;
    try {
        navigationPreference = localStorage.getItem(navigationKey);
    } catch {
        // Navigation still works when the browser disallows local storage.
    }
    const updateNavigation = () => {
        const navigationCollapsed = navigationPreference === 'true' ||
            (navigationPreference === null && /^\/plans\/[0-9a-f-]{36}(\/|$)/i.test(location.pathname));
        const navigation = document.getElementById('desktop-navigation');
        if (navigation) navigation.dataset.collapsed = String(navigationCollapsed);
        for (const toggle of document.querySelectorAll('[data-navigation-toggle]')) {
            toggle.setAttribute('aria-expanded', String(!navigationCollapsed));
            toggle.querySelector('[data-navigation-label]').textContent = navigationCollapsed ? 'Show menu' : 'Hide menu';
        }
    };
    document.addEventListener('click', event => {
        if (!event.target.closest('[data-navigation-toggle]')) return;
        navigationPreference = document.getElementById('desktop-navigation')?.dataset.collapsed === 'true' ? 'false' : 'true';
        updateNavigation();
        try {
            localStorage.setItem(navigationKey, navigationPreference);
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
