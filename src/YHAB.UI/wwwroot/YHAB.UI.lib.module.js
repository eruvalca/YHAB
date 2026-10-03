import { initializeNavigation } from './Layout/MainLayout.razor.js';

export function afterWebStarted(blazor) {
    initializeNavigation(blazor);
}
