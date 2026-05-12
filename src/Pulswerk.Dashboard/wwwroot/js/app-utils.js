/**
 * Global utility functions for the Pulswerk Dashboard
 */

/**
 * Returns a FontAwesome icon based on the point type
 */
function getPointIcon(type) {
    if (!type) return '<i class="fas fa-tag"></i>';
    const t = type.toLowerCase();
    
    if (t.includes('analog')) return '<i class="fas fa-microchip"></i>';
    if (t.includes('binary') || t.includes('bool')) return '<i class="fas fa-toggle-on"></i>';
    if (t.includes('multistate') || t.includes('enum')) return '<i class="fas fa-list-ul"></i>';
    if (t.includes('temp')) return '<i class="fas fa-thermometer-half"></i>';
    if (t.includes('power') || t.includes('energy') || t.includes('kw')) return '<i class="fas fa-bolt"></i>';
    if (t.includes('water') || t.includes('flow')) return '<i class="fas fa-tint"></i>';
    if (t.includes('pressure')) return '<i class="fas fa-tachometer-alt"></i>';
    if (t.includes('fan') || t.includes('air')) return '<i class="fas fa-wind"></i>';
    if (t.includes('pump')) return '<i class="fas fa-fill-drip"></i>';
    if (t.includes('calendar')) return '<i class="fas fa-calendar-alt"></i>';
    if (t.includes('schedule')) return '<i class="fas fa-clock"></i>';
    
    return '<i class="fas fa-tag"></i>';
}

/**
 * Formats a number with local decimal separators and fixed decimal places
 */
function formatNumber(val, decimals = 2) {
    if (val === null || val === undefined || val === '---') return '---';
    const num = typeof val === 'number' ? val : parseFloat(val);
    if (isNaN(num)) return val;
    return num.toLocaleString(typeof currentLang !== 'undefined' ? currentLang : undefined, { 
        minimumFractionDigits: decimals, 
        maximumFractionDigits: decimals 
    });
}

/**
 * Toggles a key in localStorage favorites and updates the UI star
 */
function toggleFavorite(key, btn) {
    let favs = JSON.parse(localStorage.getItem('deziko_favorites') || '[]');
    const index = favs.indexOf(key);
    
    if (index > -1) {
        favs.splice(index, 1);
        if (btn) {
            btn.querySelector('i').className = 'far fa-star';
            btn.classList.remove('active');
        }
    } else {
        favs.push(key);
        if (btn) {
            btn.querySelector('i').className = 'fas fa-star text-amber-400';
            btn.classList.add('active');
        }
    }
    
    localStorage.setItem('deziko_favorites', JSON.stringify(favs));
    
    // If we are on the Index page, reload the favorites list
    if (typeof loadFavorites === 'function') {
        loadFavorites();
    }
}

/**
 * Updates the star icon state based on localStorage
 */
function updateStarState(key, btn) {
    if (!btn) return;
    const favs = JSON.parse(localStorage.getItem('deziko_favorites') || '[]');
    if (favs.includes(key)) {
        btn.querySelector('i').className = 'fas fa-star text-amber-400';
        btn.classList.add('active');
    } else {
        btn.querySelector('i').className = 'far fa-star';
        btn.classList.remove('active');
    }
}
