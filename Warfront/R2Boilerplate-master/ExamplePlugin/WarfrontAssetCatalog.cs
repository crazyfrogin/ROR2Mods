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
        private static readonly List<GameObject> _commanderMasterPrefabs = new List<GameObject>();

        internal static bool IsLoaded { get; private set; }

        internal static GameObject ShrineCombatPrefab { get; private set; }
        internal static GameObject ChestSmallPrefab { get; private set; }

        internal static IReadOnlyList<GameObject> ReconMasterPrefabs => _reconMasterPrefabs;
        internal static IReadOnlyList<GameObject> AssaultMasterPrefabs => _assaultMasterPrefabs;
        internal static IReadOnlyList<GameObject> HeavyMasterPrefabs => _heavyMasterPrefabs;
        internal static IReadOnlyList<GameObject> CommanderMasterPrefabs => _commanderMasterPrefabs;

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

            var beetleGuardMaster = TryLoadPrefab("RoR2/Base/BeetleGuardAlly/BeetleGuardMaster.prefab");
            if (!beetleGuardMaster) beetleGuardMaster = TryLoadPrefab("RoR2/Base/Beetle/BeetleGuardMaster.prefab");
            var elderLemurianMaster = TryLoadPrefab("RoR2/Base/LemurianBruiser/LemurianBruiserMaster.prefab");
            var greaterWispMaster = TryLoadPrefab("RoR2/Base/GreaterWisp/GreaterWispMaster.prefab");
            var clayTemplarMaster = TryLoadPrefab("RoR2/Base/ClayBruiser/ClayBruiserMaster.prefab");
            var bisonMaster = TryLoadPrefab("RoR2/Base/Bison/BisonMaster.prefab");
            var vagrantMaster = TryLoadPrefab("RoR2/Base/Vagrant/VagrantMaster.prefab");
            var parentMaster = TryLoadPrefab("RoR2/Base/Parent/ParentMaster.prefab");
            var bellMaster = TryLoadPrefab("RoR2/Base/Bell/BellMaster.prefab");

            AddIfValid(_reconMasterPrefabs, beetleMaster);
            AddIfValid(_reconMasterPrefabs, lemurianMaster);

            AddIfValid(_assaultMasterPrefabs, beetleMaster);
            AddIfValid(_assaultMasterPrefabs, lemurianMaster);
            AddIfValid(_assaultMasterPrefabs, wispMaster);

            AddIfValid(_heavyMasterPrefabs, golemMaster);
            AddIfValid(_heavyMasterPrefabs, lemurianMaster);

            AddIfValid(_commanderMasterPrefabs, beetleGuardMaster);
            AddIfValid(_commanderMasterPrefabs, elderLemurianMaster);
            AddIfValid(_commanderMasterPrefabs, greaterWispMaster);
            AddIfValid(_commanderMasterPrefabs, clayTemplarMaster);
            AddIfValid(_commanderMasterPrefabs, golemMaster);
            AddIfValid(_commanderMasterPrefabs, bisonMaster);
            AddIfValid(_commanderMasterPrefabs, vagrantMaster);
            AddIfValid(_commanderMasterPrefabs, parentMaster);
            AddIfValid(_commanderMasterPrefabs, bellMaster);

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
