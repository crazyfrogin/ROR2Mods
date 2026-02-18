## Changelog

### 1.4.0

- Added Possession Protocol mode:
  - Only one active protocol mimic at a time with FIFO queueing for eligible dead players.
  - Excludes last-alive deaths and active protocol mimic kills from conversion eligibility.
  - Active mimic cash-out now respawns the mimic owner at kill location with safe fallback.
  - Active mimic death now advances the queue.
- Added left-click mimic voice trigger that reuses Mirage synced audio stream playback.
- Added hard dependency on Mirage for voice playback components.

### 1.3.0

- Updated to support ``MirageCore v1.0.3``.

### 1.2.0

- Added a missing check that I had in the original Mirage.

### 1.1.2

- Fixed using the wrong MaskedPlayerEnemy prefab.

### 1.1.1

~~- Fixed using the wrong MaskedPlayerEnemy prefab.~~

### 1.1.0

- Added a missing soft dependency BepInEx flag.

### 1.0.0

- Initial release.