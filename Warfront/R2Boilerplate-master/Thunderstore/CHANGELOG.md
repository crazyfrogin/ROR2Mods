## 0.4.0

- Replaced interactable enemy nodes with kill-only Command Elites (Relay, Forge, Siren, Cache).
- Added commander tether + anti-kite behavior and commander aura pulses while alive.
- Added commander-death response logic: retaliation micro-wave, doctrine pivot, breach deployment, or reserve credit dump.
- Added type-based commander reward model with stage/difficulty/player scaling.
- Added flanker role to assault role rotation and doctrine composition.
- Added runtime role steering controller for objective-first contesters, artillery spacing discipline, flank paths, peeler pressure, hunter squad targeting, and anchor hold behavior.
- Added doctrine/role/event buff package application flow with event buff windows during assaults, breaches, and retaliation events.
- Updated HUD/network snapshot payload to sync active commander count and commander type mask.

## 0.3.0

- Implemented Phase 3 campaign hooks with stage-to-stage adaptation signals.
- Added doctrine profile generation per stage: Balanced, Swarm Front, Artillery Front, Hunter Cell, Siege Front, and Disruption Front.
- Added bounded/decayed adaptation logic to prevent doctrine hard-locking across consecutive stages.
- Integrated doctrine influence into operation rolls, role selection, director cadence, and pulse composition.
- Extended HUD/network snapshot with doctrine state.

## 0.2.0

- Expanded Warfront operations to a 12 warning / 8 anomaly roster.
- Added V1 role orchestration (Contester, Peeler, Artillery, Hunter, Anchor) for assault identity.
- Added Siren and Spawn Cache node archetypes while keeping all node visuals on Shrine of Combat.
- Added anti-lone-wolf objective pressure and revive mercy window fairness systems.
- Added V1 tuning config entries for role budget and fairness behavior.
- Updated HUD to be smaller, show role/fairness state, and sit below money/lunar coin UI.
- Updated network snapshot payload to include dominant role and fairness indicators.

## 0.1.0

- Renamed project/plugin to WarfrontDirector.
- Replaced sample item demo with server-authoritative Warfront runtime scaffold.
- Added contested teleporter logic with rollback cap and immunity.
- Added assault/breather cadence, intensity phases, and breach escalation.
- Added stage operation rolls (6 warnings, 4 anomalies in MVP pool).
- Added enemy nodes (Relay + Forge) using shrine stand-ins.
- Added minimal HUD overlay for phase/intensity/contest visibility.