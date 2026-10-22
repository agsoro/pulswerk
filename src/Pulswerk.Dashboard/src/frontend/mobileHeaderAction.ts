import { useEffect, useState } from 'preact/hooks';

/**
 * Lets a page contribute a single action button to the mobile shell header
 * (rendered right after the "Live" pill). Pages register an action on mount
 * and clear it on unmount.
 */
export interface MobileHeaderAction {
    icon: string;
    title: string;
    onClick: () => void;
}

type Listener = () => void;

let action: MobileHeaderAction | null = null;
const listeners = new Set<Listener>();

export function setMobileHeaderAction(next: MobileHeaderAction | null) {
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