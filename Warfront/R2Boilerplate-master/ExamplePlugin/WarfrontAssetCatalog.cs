using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace WarfrontDirector
{
    internal static class WarfrontAssetCatalog
    {
        private static readonly List<GameObject> _reconMasterPrefabs = new List<GameObject>();
        private static readonly List<GameObject> _assaultMasterPrefabs = new List<GameObject>();
        private static readonly List<GameObject> _heavyMasterPrefabs = new List<GameObject>();

        internal static bool IsLoaded { get; private set; }

        internal static GameObject ShrineCombatPrefab { get; private set; }
        internal static GameObject ChestSmallPrefab { get; private set; }

        internal static IReadOnlyList<GameObject> ReconMasterPrefabs => _reconMasterPrefabs;
        internal static IReadOnlyList<GameObject> AssaultMasterPrefabs => _assaultMasterPrefabs;
        internal static IReadOnlyList<GameObject> HeavyMasterPrefabs => _heavyMasterPrefabs;

        internal static void Load()
        {
            if (IsLoaded)
            {
                return;
            }

            ShrineCombatPrefab = TryLoadPrefab("RoR2/Base/ShrineCombat/ShrineCombat.prefab");
            ChestSmallPrefab = TryLoadPrefab("RoR2/Base/Chest1/Chest1.prefab");

            var beetleMaster = TryLoadPrefab("RoR2/Base/Beetle/BeetleMaster.prefab");
            var lemurianMaster = TryLoadPrefab("RoR2/Base/Lemurian/LemurianMaster.prefab");
            var wispMaster = TryLoadPrefab("RoR2/Base/Wisp/WispMaster.prefab");
            var golemMaster = TryLoadPrefab("RoR2/Base/Golem/GolemMaster.prefab");

            AddIfValid(_reconMasterPrefabs, beetleMaster);
            AddIfValid(_reconMasterPrefabs, lemurianMaster);

            AddIfValid(_assaultMasterPrefabs, beetleMaster);
            AddIfValid(_assaultMasterPrefabs, lemurianMaster);
            AddIfValid(_assaultMasterPrefabs, wispMaster);

            AddIfValid(_heavyMasterPrefabs, golemMaster);
            AddIfValid(_heavyMasterPrefabs, lemurianMaster);

            IsLoaded = true;
        }

        private static void AddIfValid(List<GameObject> list, GameObject prefab)
        {
            if (prefab && !list.Contains(prefab))
            {
                list.Add(prefab);
            }
        }

        private static GameObject TryLoadPrefab(string address)
        {
            try
            {
                return Addressables.LoadAssetAsync<GameObject>(address).WaitForCompletion();
            }
            catch (System.Exception e)
            {
                Log.Warning($"Failed to load prefab at '{address}': {e.Message}");
                return null;
            }
        }
    }
}
