# Deck Duel

A 1v1 PvP mode for Risk of Rain 2 where your **build is a deck** of items bought with a point budget, dealt out over short timed rounds.

## How It Works

1. **Build a deck** — Pick a survivor, then spend 40 points on up to 18 item "cards" (plus optional equipment and drones).
2. **Fight in rounds** — Best of 3. Each round is 90 seconds with a 15-second warmup.
3. **Cards drip in** — Start with 6 cards, gain 1 more every 12 seconds. At Sudden Death (60s), all remaining cards are dumped.
4. **Arena ring** — A shrinking boundary forces engagement. Out-of-bounds deals damage.

## Item Costs

| Tier | Cost |
|------|------|
| White (Common) | 1 |
| Green (Uncommon) | 3 |
| Red (Legendary) | 7 |
| Yellow (Boss) | 6 |
| Purple (Void) | 6 |
| Blue (Lunar) | 5 |
| Equipment | 6 |
| Lunar Equipment | 7 |
| Drones | 2–7 (varies) |

**Stacking:** 1st copy = base cost, 2nd = ×1.5 (rounded up), 3rd+ = ×2.

## Installation

1. Install [BepInEx](https://thunderstore.io/package/bbepis/BepInExPack/) and [R2API](https://thunderstore.io/package/tristanmcpherson/R2API/) dependencies.
2. Copy `DeckDuel.dll` into `BepInEx/plugins/`.
3. Launch the game. A config file will be generated at `BepInEx/config/DeckDuel.DeckDuel.cfg`.

## Building from Source

```
dotnet build DeckDuel\DeckDuel.csproj
```

Output: `DeckDuel\bin\Debug\netstandard2.1\DeckDuel.dll`

## Configuration

All settings are editable in the BepInEx config file:

- **Budget** — Point budget (default 40), max deck size (18), equipment toggle
- **Tier Costs** — Per-tier point costs
- **Stacking** — Multipliers for duplicate items
- **Banlist** — Comma-separated item/equipment internal names to ban
- **Match** — Best-of count, round/warmup/sudden-death durations, card interval
- **Arena** — Preferred scenes, radius, shrink rates, out-of-bounds DPS

## Dependencies

- BepInEx 5.4+
- R2API: Items, Language, Networking, Prefab, ContentManagement
- HookGenPatcher
- MMHOOK.RoR2