export function initLogsPage() {
    const container = document.querySelector('[data-testid="log-container"]');
    if (container) {
        container.scrollTop = container.scrollHeight;
    }
    const select = document.getElementById('logLevelSelect');
    if (select) {
        // Retrieve saved preference
        const savedLevel = localStorage.getItem('logLevelPref');
        if (savedLevel) {
            select.value = savedLevel;
        }
        select.addEventListener('change', () => {
            const level = select.value;
            localStorage.setItem('logLevelPref', level);
            const entries = document.querySelectorAll('.log-entry');
            entries.forEach((el) => {
                const entryLevel = el.getAttribute('data-level');
                let show = true;
                if (level === 'info') {
                    show = entryLevel !== 'debug';
                }
                else if (level === 'warning') {
                    show = entryLevel === 'warning' || entryLevel === 'error';
                }
                else if (level === 'error') {
                    show = entryLevel === 'error';
                }
                if (show) {
                    el.style.display = '';
                }
                else {
                    el.style.display = 'none';
                }
            });
            if (container) {
                container.scrollTop = container.scrollHeight;
            }
        });
        // Initial filter application
        select.dispatchEvent(new Event('change'));
    }
}
initLogsPage();
