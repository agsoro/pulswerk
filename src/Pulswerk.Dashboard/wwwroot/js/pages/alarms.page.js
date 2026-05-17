/// <reference path="../types/pulswerk.d.ts" />
let _ackAlarmId = '';
let _bacnetAckKey = '';
export function openAck(btn) {
    _ackAlarmId = btn.dataset.alarmId || '';
    _bacnetAckKey = btn.dataset.bacnetAckKey || '';
    const card = btn.closest('[class*="grid"]');
    const alarmType = card?.querySelector('.font-bold')?.textContent?.trim() ?? _ackAlarmId;
    document.getElementById('ackAlarmName').textContent = alarmType;
    document.getElementById('ackBacnetNote').style.display = _bacnetAckKey ? 'block' : 'none';
    document.getElementById('ackLocalOnly').style.display = _bacnetAckKey ? 'none' : 'block';
    document.getElementById('ackComment').value = '';
    document.getElementById('ackModal').style.display = 'flex';
    setTimeout(() => document.getElementById('ackComment')?.focus(), 50);
}
export function closeAck() {
    document.getElementById('ackModal').style.display = 'none';
    const s = document.getElementById('ackStatus');
    s.className = 'ack-status-msg';
    s.textContent = '';
    const btn = document.getElementById('ackConfirmBtn');
    btn.disabled = false;
    btn.innerHTML = '<i class="fas fa-check"></i> Confirm';
    document.getElementById('ackCancelBtn').disabled = false;
}
export async function submitAck() {
    const btn = document.getElementById('ackConfirmBtn');
    const cancelBtn = document.getElementById('ackCancelBtn');
    const statusEl = document.getElementById('ackStatus');
    btn.disabled = cancelBtn.disabled = true;
    statusEl.className = 'ack-status-msg sending';
    statusEl.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Sending…';
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    const comment = document.getElementById('ackComment').value.trim();
    try {
        const resp = await fetch('?handler=Ack', {
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
        }
        else {
            statusEl.className = 'ack-status-msg error';
            statusEl.innerHTML = '<i class="fas fa-exclamation-circle"></i> Failed: ' + (result.error ?? 'Unknown error');
            btn.disabled = cancelBtn.disabled = false;
        }
    }
    catch (err) {
        statusEl.className = 'ack-status-msg error';
        statusEl.innerHTML = '<i class="fas fa-exclamation-circle"></i> Request failed: ' + err.message;
        btn.disabled = cancelBtn.disabled = false;
    }
}
export function initAlarmsPage() {
    const ackModal = document.getElementById('ackModal');
    if (ackModal) {
        ackModal.addEventListener('click', e => {
            if (e.target === ackModal)
                closeAck();
        });
    }
    window.openAck = openAck;
    window.closeAck = closeAck;
    window.submitAck = submitAck;
}
initAlarmsPage();
