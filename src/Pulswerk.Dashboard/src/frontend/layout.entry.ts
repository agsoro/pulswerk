const originalFetch = window.fetch;
window.fetch = function (input) {
    const url = typeof input === 'string' ? input : (input instanceof URL ? input.toString() : (input && typeof input === 'object' && 'url' in input ? (input as any).url : ''));
    if (url && url.includes('api/tree')) {
        console.warn(`[DEBUG] fetch('api/tree') called! Stack trace:`, new Error().stack);
    }
    return originalFetch.apply(this, arguments as any);
};

import './i18n';
import './app-utils';
import './timewindow';
import './base';
import './modals';

console.log('Pulswerk Layout Bundle Initialized.');
