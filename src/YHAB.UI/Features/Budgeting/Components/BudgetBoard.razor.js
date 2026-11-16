// The board moves the category editor beside the table on wide screens and
// beneath the selected row below the same breakpoint the stylesheet stacks on,
// so placement is decided from one media query instead of a guessed width.
const stacked = matchMedia('(max-width: 75rem)');

export function inlineCategoryDetails() {
    return stacked.matches;
}
