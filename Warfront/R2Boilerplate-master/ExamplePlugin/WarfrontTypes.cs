using UnityEngine;

namespace WarfrontDirector
{
    internal enum WarfrontPhase
    {
        Recon,
        Skirmish,
        Assault,
        Overwhelm,
        Cooldown,
        Breach
    }

    internal enum WarWarning
    {
        Sappers,
        PhalanxDoctrine,
        ArtilleryDoctrine,
        HunterKiller,
        MedicNet,
        Attrition,
        SiegeEngine,
        PackTactics,
        SignalJamming,
        ReinforcedVanguard,
        ExecutionOrder,
        SupplyLine
    }

    internal enum WarAnomaly
    {
        SilentMinute,
        WarDrums,
        FalseLull,
        CommandConfusion,
        Blackout,
        CounterIntel,
        BlitzOrder,
        IronRain
    }

    internal enum WarfrontNodeType
    {
        Relay,
        Forge,
        Siren,
        SpawnCache
    }

    internal enum WarfrontRole
    {
        None,
        Contester,
        Peeler,
        Artillery,
        Flanker,
        Hunter,
        Anchor
    }

    internal enum WarfrontDoctrineProfile
    {
        Balanced,
        SwarmFront,
        ArtilleryFront,
        HunterCell,
        SiegeFront,
        DisruptionFront
    }

    internal readonly struct WarfrontOperationRoll
    {
        internal readonly WarWarning WarningOne;
        internal readonly WarWarning WarningTwo;
        internal readonly WarAnomaly Anomaly;

        internal WarfrontOperationRoll(WarWarning warningOne, WarWarning warningTwo, WarAnomaly anomaly)
        {
            WarningOne = warningOne;
            WarningTwo = warningTwo;
            Anomaly = anomaly;
        }
    }

    internal struct WarfrontHudSnapshot
    {
        internal bool Active;
        internal WarfrontPhase Phase;
        internal WarfrontRole DominantRole;
        internal WarfrontDoctrineProfile Doctrine;
        internal float Intensity;
        internal float ContestDelta;
        internal float ChargeFraction;
        internal bool AssaultActive;
        internal bool BreachActive;
        internal bool MercyActive;
        internal float LoneWolfPressure;
        internal float WindowTimeRemaining;
        internal string OperationSummary;
        internal int ActiveCommanders;
        internal byte CommanderTypeMask;
        internal Color ContestColor;
    }
}
