export function initLogsPage(): void {
    const container = document.querySelector('[data-testid="log-container"]');
    if (container) {
        container.scrollTop = container.scrollHeight;
    }
}

initLogsPage();
