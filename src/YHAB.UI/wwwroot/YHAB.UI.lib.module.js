import { initializeInputMode } from './input-mode.js';
import { initializeNavigation } from './Layout/MainLayout.razor.js';
import { initializePwa } from './pwa.js';

export function afterWebStarted(blazor) {
    initializeInputMode();
    initializeNavigation(blazor);
    initializePwa(blazor);
}
