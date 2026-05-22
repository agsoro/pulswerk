export function initLogsPage(): void {
    const container = document.querySelector('[data-testid="log-container"]');
    if (container) {
        container.scrollTop = container.scrollHeight;
    }

    const select = document.getElementById('logLevelSelect') as HTMLSelectElement;
    if (select) {
        const params = new URLSearchParams(window.location.search);
        const urlLevel = params.get('level');
        const savedPref = localStorage.getItem('logLevelPref') || 'all';

        if (!urlLevel) {
            window.location.replace('/plswk/Logs?level=' + savedPref);
            return;
        }

        if (urlLevel !== savedPref) {
            localStorage.setItem('logLevelPref', urlLevel);
        }

        select.value = urlLevel;

        select.addEventListener('change', () => {
            const level = select.value;
            localStorage.setItem('logLevelPref', level);
            window.location.href = '/plswk/Logs?level=' + level;
        });
    }
}

initLogsPage();
