const compact = matchMedia('(max-width: 43.75rem)');

export function compactRegister() {
    return compact.matches;
}
