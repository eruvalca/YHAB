import { initializeInputMode } from './input-mode.js';
import { initializeNavigation } from './Layout/MainLayout.razor.js';
import { initializePwa } from './pwa.js';
import { initializeSortableLists } from './sortable-lists.js';
import { initializeShareCodeCopy } from './Features/Budgeting/Pages/PlanSettings.razor.js';

export function afterWebStarted(blazor) {
    initializeInputMode();
    initializeSortableLists();
    initializeNavigation(blazor);
    initializePwa(blazor);
    initializeShareCodeCopy();
}
