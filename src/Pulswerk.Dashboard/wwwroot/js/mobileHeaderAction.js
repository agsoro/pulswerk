import { useEffect, useState } from 'preact/hooks';
let action = null;
const listeners = new Set();
export function setMobileHeaderAction(next) {
    action = next;
    listeners.forEach(listener => listener());
}
export function useMobileHeaderAction() {
    const [value, setValue] = useState(action);
    useEffect(() => {
        const listener = () => setValue(action);
        listeners.add(listener);
        listener();
        return () => {
            listeners.delete(listener);
        };
    }, []);
    return value;
}
