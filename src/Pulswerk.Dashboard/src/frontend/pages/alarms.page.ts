/// <reference path="../types/pulswerk.d.ts" />

let _ackAlarmId = '';
let _bacnetAckKey = '';

export function openAck(btn: HTMLElement): void {
    _ackAlarmId  = btn.dataset.alarmId || '';
    _bacnetAckKey = btn.dataset.bacnetAckKey || '';
    const card = btn.closest('[class*="grid"]');
    const alarmType = card?.querySelector('.font-bold')?.textContent?.trim() ?? _ackAlarmId;
    document.getElementById('ackAlarmName')!.textContent = alarmType;
    document.getElementById('ackBacnetNote')!.style.display = _bacnetAckKey ? 'block' : 'none';
    document.getElementById('ackLocalOnly')!.style.display = _bacnetAckKey ? 'none' : 'block';
    (document.getElementById('ackComment') as HTMLTextAreaElement).value = '';
    document.getElementById('ackModal')!.style.display = 'flex';
    setTimeout(() => document.getElementById('ackComment')?.focus(), 50);
}

export function closeAck(): void {
    document.getElementById('ackModal')!.style.display = 'none';
    const s = document.getElementById('ackStatus')!;
    s.className = 'ack-status-msg'; s.textContent = '';
    const btn = document.getElementById('ackConfirmBtn') as HTMLButtonElement;
    btn.disabled = false;
    btn.innerHTML = '<i class="fas fa-check"></i> Confirm';
    (document.getElementById('ackCancelBtn') as HTMLButtonElement).disabled = false;
}

export async function submitAck(): Promise<void> {
    const btn       = document.getElementById('ackConfirmBtn') as HTMLButtonElement;
    const cancelBtn = document.getElementById('ackCancelBtn') as HTMLButtonElement;
    const statusEl  = document.getElementById('ackStatus')!;

    btn.disabled = cancelBtn.disabled = true;
    statusEl.className   = 'ack-status-msg sending';
    statusEl.innerHTML   = '<i class="fas fa-spinner fa-spin"></i> Sending…';

    const token   = (document.querySelector('input[name="__RequestVerificationToken"]') as HTMLInputElement)?.value ?? '';
    const comment = (document.getElementById('ackComment') as HTMLTextAreaElement).value.trim();

    try {
        const resp   = await fetch('?handler=Ack', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({ alarmId: _ackAlarmId, comment, bacnetAckKey: _bacnetAckKey })
        });
        const result = await resp.json();

        if (result.success) {
            const bacnetNote = result.bacnetAcked
                ? ' <span class="opacity-70 text-[0.8em]">· BACnet ACK sent</span>'
                : (_bacnetAckKey ? ' <span class="opacity-50 text-[0.8em]">· BACnet ACK skipped (context lost)</span>' : '');
            statusEl.className = 'ack-status-msg success';
            statusEl.innerHTML = '<i class="fas fa-check-circle"></i> Acknowledged' + bacnetNote;
            setTimeout(() => { closeAck(); location.reload(); }, 1400);
        } else {
            statusEl.className = 'ack-status-msg error';
            statusEl.innerHTML = '<i class="fas fa-exclamation-circle"></i> Failed: ' + (result.error ?? 'Unknown error');
            btn.disabled = cancelBtn.disabled = false;
        }
    } catch (err: any) {
        statusEl.className = 'ack-status-msg error';
        statusEl.innerHTML = '<i class="fas fa-exclamation-circle"></i> Request failed: ' + err.message;
        btn.disabled = cancelBtn.disabled = false;
    }
}

export async function resetAlarm(btn: HTMLElement): Promise<void> {
    const alarmId = btn.dataset.alarmId;
    if (!alarmId) return;

    if (!confirm('Are you sure you want to reset this alarm?')) return;

    const token = (document.querySelector('input[name="__RequestVerificationToken"]') as HTMLInputElement)?.value ?? '';
    const icon = btn.querySelector('i');
    if (icon) icon.className = 'fas fa-spinner fa-spin';
    btn.style.pointerEvents = 'none';
    btn.style.opacity = '0.7';

    try {
        const resp = await fetch('?handler=Reset', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({ alarmId })
        });
        const result = await resp.json();

        if (result.success) {
            location.reload();
        } else {
            alert('Failed to reset alarm: ' + (result.error ?? 'Unknown error'));
            if (icon) icon.className = 'fas fa-redo';
            btn.style.pointerEvents = '';
            btn.style.opacity = '1';
        }
    } catch (err: any) {
        alert('Request failed: ' + err.message);
        if (icon) icon.className = 'fas fa-redo';
        btn.style.pointerEvents = '';
        btn.style.opacity = '1';
    }
}

export function initAlarmsPage(): void {
    const ackModal = document.getElementById('ackModal');
    if (ackModal) {
        ackModal.addEventListener('click', e => {
            if (e.target === ackModal) closeAck();
        });
    }

    (window as any).openAck = openAck;
    (window as any).closeAck = closeAck;
    (window as any).submitAck = submitAck;
    (window as any).resetAlarm = resetAlarm;
}

initAlarmsPage();
