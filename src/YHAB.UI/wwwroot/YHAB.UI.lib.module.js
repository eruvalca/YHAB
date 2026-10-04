import { initializeNavigation } from './Layout/MainLayout.razor.js';
import { initializePwa } from './pwa.js';

export function afterWebStarted(blazor) {
    initializeNavigation(blazor);
    initializePwa(blazor);
}
