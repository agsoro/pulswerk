# EMS → Wallbox control: findings

Investigation of why EMS setpoints do not reach the wallboxes.

## Control path (confirmed)

```
EmsService.LoopAsync (10 s)
  └─ EvaluateDispatchAsync → EnergyDispatchEngine.Dispatch
       └─ _telemetryWriter(key, value)            // key = consumer.ForcePowerKey
            └─ DashboardDataService.WriteValueAsync
                 ├─ IdentifyDeviceFromTelemetryKey  // longest DeviceConfig.Id prefix
                 ├─ Config.Connections check
                 └─ Drivers[device.Name].Write
                      └─ OcppDriver.Write          // .GetAwaiter().GetResult()
                           └─ OcppManagerService.SetChargingLimitAsync
                                └─ SendChargingProfileMessageAsync → OCPP 1.6-J SetChargingProfile
```

The chain is wired correctly end-to-end. The failure is almost certainly in **key
resolution** or **silent partial success**, not in a missing link.

## Ranked root-cause candidates

| # | Where | Problem |
|---|-------|---------|
| 1 | `src/Pulswerk.Dashboard/Services/WriteBackService.cs` | `ForcePowerKey` must be exactly `{ocppDeviceId}_force_power`. Any stale/renamed id (e.g. `ocpp-master_…`) → write rejected with *"no device found for key"*. Nothing surfaces this in the UI. |
| 2 | `src/Pulswerk.Dashboard/Services/WriteBackService.cs` | Driver lookup is by `device.Name`, not device type. A name mismatch → *"driver is not an IDeviceWriter"*. |
| 3 | `src/Pulswerk.Drivers/Ocpp/OcppManagerService.cs` | `SetChargingLimitAsync` returns `okMax \|\| okDefault \|\| okTx` — a partial failure still reports success. `SendChargingProfileMessageAsync` never awaits the CallResult (only checks the socket write), while `ClearChargingProfileAsync` waits 10 s. |
| 4 | `src/Pulswerk.Drivers/Ocpp/OcppManagerService.cs` | `ClearChargingProfile` payload is only `{ connectorId }` — no `purpose`/`stackLevel`. Many chargers reject or clear too broadly. |
| 5 | `src/Pulswerk.Drivers/Ocpp/OcppDriver.cs` | `Write` blocks on `.GetAwaiter().GetResult()` over WebSocket I/O inside the EMS loop → stall/deadlock risk. |
| 6 | `src/Pulswerk.Ems/EmsService.cs` | `Enabled == false` only zeroes `AllocatedPowerKw` in memory; no setpoint is written, so the wallbox keeps the last limit. |
| 7 | `src/Pulswerk.Ems/EmsService.cs` | `IsActivelyDemanding` uses `ActualPowerKw`; a wrong/empty `ActualPowerKey` → actual = 0 → consumer never ramps. |
| 8 | `src/Pulswerk.Ems/EmsService.cs` | Writes only fire when the setpoint moves ≥ 0.3 kW or 60 s heartbeat elapsed (`LastWrittenSetpointKw` starts at `-1.0`). |

## Legacy / dead code safe to delete

- Demo-consumer filter in `LoadConfiguration` (`base-building-infrastructure`, `base-server-room`).
- `SaveConfiguration` fallback `if (Consumers.Count == 0) Consumers = consumers;` — re-adds invalid entries.
- Hardcoded fallback meter keys: `meter-main-a_power`, `meter-main_power`, `pv-rooftop_power`, `glueck-pv_power`, `solis-pv_power`.
- Unused i18n keys `ems_wb_force_power_key` / `ems_wb_actual_power_key` in `src/Pulswerk.Dashboard/src/frontend/i18n.ts` (`ems.tsx` uses hardcoded English labels).
- `OcppMasterDriver` write path — read-only by design; the "EMS writes to the OCPP master" model is obsolete.

**Keep:** `TelemetryKeys.PowerLimitPct` — still used by Solis, Glueck, SunSpec and SmaDatamanager drivers.

## Recommended fix order

1. Log + surface the rejection reason from `WriteValueAsync` (turns a silent no-op into a visible error).
2. Validate `ForcePowerKey`/`ActualPowerKey` against real device ids when saving the consumer config.
3. Await CallResults in `SetChargingLimitAsync`; return failure unless the intended profile was accepted.
4. Send a specific `ClearChargingProfile` (`purpose` + `stackLevel`).
5. Make `OcppDriver.Write` non-blocking.
6. Write an explicit unrestricted setpoint when EMS is disabled.
7. Delete the legacy items above.

## Resolution status

All seven items are implemented.

| # | Change |
|---|--------|
| 1 | `WriteResult` (`Pulswerk.Core/IDeviceDriver.cs`) carries `Success` + `Error`. `WriteValueAsync`/`WriteComplexValueAsync` return it; `/api/write`, `/api/write-complex` and `OnPostWrite` return `{ success, error }`; `modals.js` shows the reason; the EMS log records it. |
| 2 | `ApiController.ValidateConsumerKeys` rejects keys that do not start with a configured `<deviceId>_` prefix (400) in both `AddOrUpdateConsumer` and `UpdateEmsConfig`. |
| 3 | `SendChargingProfileMessageAsync` registers a `TaskCompletionSource` and awaits the CallResult (`CallResultTimeout` = 10 s). `SetChargingLimitAsync` returns `okMax && okDefault && okTx`. |
| 4 | `ClearChargingProfileAsync(chargePointId, connectorId, purpose, stackLevel)` sends only the fields that are set. Unrestricted mode clears `ChargePointMaxProfile`/1, `TxDefaultProfile`/1 and `TxProfile`/10; curtailed mode clears the stale `TxProfile`/10 first. |
| 5 | `IDeviceWriter.WriteAsync` (default delegates to `Write`). `OcppDriver.WriteAsync` awaits the manager; `Write` is a thin sync wrapper. `ControlEngine.EvaluateDueAsync` uses `WaitAsync` + `WriteAsync`. |
| 6 | `EmsService.ReleaseControlAsync` writes `-1.0` (unrestricted) to every controllable consumer when EMS is disabled, with a 60 s heartbeat guard. |
| 7 | Removed: demo-consumer filter, `SaveConfiguration` fallback, hardcoded meter-key defaults (`EnergySourcesConfig` properties, `LoadConfiguration`, `ems.tsx`), unused `ems_wb_*` i18n keys, `OcppMasterDriver`'s `IDeviceWriter` implementation. |

Also fixed while in the area: the "Configure System" modal was unreachable
(`showConfigModal` was never set to `true`). It now opens from a header button and
hydrates its fields from the current snapshot instead of hardcoded defaults.

## Review follow-up

A review of the changes above found that awaiting `SetChargingLimitAsync` from inside
the OCPP receive loop deadlocked: the CALLRESULT it waits for can only be read by that
same loop, so every `MeterValues`-triggered limit change stalled for the full
`CallResultTimeout` (10 s) and delayed the `MeterValues` response.

| # | Change |
|---|--------|
| 1 | `ProcessOcppMessage` is now synchronous and dispatches CALL handling to a background task, so the receive loop keeps reading CALLRESULT/CALLERROR frames while a handler waits for one. Handlers are chained per charge point (`_callChains`) so they still run in arrival order, and `SendMessageAsync` serializes writes per socket via `_sendLocks`, since sends now originate from both the loop and background handlers. |
| 2 | CALLRESULT handling parses the payload `status` (`IsCallResultAccepted`); `SetChargingProfile`/`ClearChargingProfile` complete their pending call with `false` on `Rejected` instead of always `true`. |
| 3 | `OcppDriver.WriteAsync` throws when a *connected* charge point rejects the profile, so `WriteValueAsync` returns `WriteResult.Fail` and the UI shows the reason. An offline charge point still records the setpoint for the next transaction. |
| 4 | `modals.ts`/`modals.js` escape `result.error` with `escapeHtml` before inserting it into `innerHTML`. |
| 5 | `vite.config.mts` builds `layout.entry.ts` again — `_Layout.cshtml` loads `layout.bundle.js` for non-Development, and the entry had been dropped, so production got a 404. |
| 6 | `EnergySourcesConfig.HasBattery` defaults to `false`, matching the UI default. |

`ProcessMeterValuesAsync` is still awaited by the `MeterValues` and `StopTransaction`
handlers, but those handlers now run off the receive loop. The synchronous
`ProcessMeterValues` overload is kept as a thin wrapper for existing callers and tests.
`IsCallResultAccepted` is covered by `tests/Pulswerk.Drivers.Tests/OcppCallResultTests.cs`.

## Caveats

- `pulswerk.json` and `pulswerk.e2e.json` contain **no** OCPP connection or device
  entries — only `pulswerk.testing.json` does (`wallbox-sim-01`, `wallbox-sim-02`).
  If the deployment runs the default config, there is no wallbox to control at all.
- `testing/ocpp-sim.js` ignores `purpose`/`connectorId` and does not implement
  `ClearChargingProfile`, so simulator runs can mask real-charger behaviour.
