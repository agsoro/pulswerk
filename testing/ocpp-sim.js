const WebSocket = require('ws');

const chargePointId = process.argv[2] || 'sim-01';
const serverUrl = process.argv[3] || `ws://localhost:5000/plswk/ocpp/${chargePointId}`;

console.log(`Starting OCPP 1.6-J Simulator for '${chargePointId}' connecting to ${serverUrl}...`);

const ws = new WebSocket(serverUrl);

let msgIdCounter = 100;
let transactionId = null;
let meterValue = 1000.0; // Wh
let chargingActive = false;
let currentLimit = 16.0; // A
let current = 0.0;
let voltage = 230.0;
let activeRfid = 'None';
let meterInterval = null;

function getMessageId() {
    return (++msgIdCounter).toString();
}

function sendCall(action, payload) {
    const id = getMessageId();
    const message = JSON.stringify([2, id, action, payload]);
    console.log(`[Sent CALL] ${action}: ${message}`);
    ws.send(message);
    return id;
}

function sendCallResult(msgId, payload) {
    const message = JSON.stringify([3, msgId, payload]);
    console.log(`[Sent RESULT] ID ${msgId}: ${message}`);
    ws.send(message);
}

ws.on('open', () => {
    console.log('Connected to Pulswerk OCPP Central System.');
    
    // 1. Send BootNotification
    sendCall('BootNotification', {
        chargePointVendor: 'PulswerkSim',
        chargePointModel: 'VirtualEVSE-v1',
        chargeBoxSerialNumber: 'SN-SIM-999',
        firmwareVersion: '1.0.0'
    });
});

ws.on('message', (data) => {
    console.log(`[Received] Raw: ${data}`);
    try {
        const parsed = JSON.parse(data);
        if (!Array.isArray(parsed) || parsed.length < 3) return;

        const msgType = parsed[0];
        const msgId = parsed[1];

        if (msgType === 2) {
            const action = parsed[2];
            const payload = parsed[3];
            handleServerCall(msgId, action, payload);
        } else if (msgType === 3) {
            const payload = parsed[2];
            console.log(`[Result Received] Message ID ${msgId}:`, payload);
            
            // If we received a transaction ID from StartTransaction response
            if (payload && payload.transactionId) {
                transactionId = payload.transactionId;
                console.log(`Transaction active. ID: ${transactionId}`);
            }
        }
    } catch (e) {
        console.error('Error handling message:', e);
    }
});

ws.on('close', () => {
    console.log('Connection closed.');
    clearInterval(meterInterval);
});

ws.on('error', (err) => {
    console.error('WebSocket Error:', err);
});

function handleServerCall(msgId, action, payload) {
    console.log(`[Server Call] ID ${msgId} | Action: ${action}`, payload);

    if (action === 'RemoteStartTransaction') {
        const idTag = payload.idTag || 'Guest';
        sendCallResult(msgId, { status: 'Accepted' });
        
        // Sim Start Charging
        startCharging(idTag);
    } 
    else if (action === 'RemoteStopTransaction') {
        sendCallResult(msgId, { status: 'Accepted' });
        
        // Sim Stop Charging
        stopCharging();
    } 
    else if (action === 'SetChargingProfile') {
        sendCallResult(msgId, { status: 'Accepted' });
        
        // Handle smart charging limit
        try {
            const schedule = payload.csChargingProfiles?.chargingSchedule;
            const period = schedule?.chargingSchedulePeriod?.[0];
            if (period && typeof period.limit === 'number') {
                currentLimit = period.limit;
                console.log(`Smart Charging: Limit set to ${currentLimit} A`);
                if (chargingActive) {
                    current = currentLimit;
                }
            }
        } catch (e) {
            console.error('Failed to parse SetChargingProfile schedule:', e);
        }
    } 
    else {
        sendCallResult(msgId, {});
    }
}

function startCharging(idTag) {
    if (chargingActive) return;
    chargingActive = true;
    activeRfid = idTag;
    current = currentLimit;
    console.log(`Starting charging session for RFID: ${idTag}`);

    // Send status
    sendCall('StatusNotification', {
        connectorId: 1,
        errorCode: 'NoError',
        status: 'Charging'
    });

    // Send StartTransaction
    sendCall('StartTransaction', {
        connectorId: 1,
        idTag: idTag,
        meterStart: Math.round(meterValue),
        timestamp: new Date().toISOString()
    });

    // Start streaming MeterValues
    meterInterval = setInterval(() => {
        // Increment meter (Power = I * V = current * 230 W)
        // 5 second interval, so Wh increment = Power * 5 / 3600
        const powerW = current * voltage;
        const deltaWh = (powerW * 5) / 3600;
        meterValue += deltaWh;

        sendCall('MeterValues', {
            connectorId: 1,
            transactionId: transactionId || 0,
            meterValue: [
                {
                    timestamp: new Date().toISOString(),
                    sampledValue: [
                        {
                            value: Math.round(meterValue).toString(),
                            measurand: 'Energy.Active.Import.Register',
                            unit: 'Wh'
                        },
                        {
                            value: Math.round(powerW).toString(),
                            measurand: 'Power.Active.Import',
                            unit: 'W'
                        },
                        {
                            value: current.toFixed(1),
                            measurand: 'Current.Import',
                            unit: 'A'
                        },
                        {
                            value: voltage.toFixed(0),
                            measurand: 'Voltage',
                            unit: 'V'
                        }
                    ]
                }
            ]
        });
    }, 5000);
}

function stopCharging() {
    if (!chargingActive) return;
    chargingActive = false;
    clearInterval(meterInterval);
    current = 0.0;
    console.log(`Stopping charging session. Consumed: ${meterValue} Wh`);

    // Send StopTransaction
    sendCall('StopTransaction', {
        transactionId: transactionId || 1000,
        meterStop: Math.round(meterValue),
        timestamp: new Date().toISOString(),
        reason: 'Local'
    });

    // Send status
    sendCall('StatusNotification', {
        connectorId: 1,
        errorCode: 'NoError',
        status: 'Available'
    });

    transactionId = null;
    activeRfid = 'None';
}
