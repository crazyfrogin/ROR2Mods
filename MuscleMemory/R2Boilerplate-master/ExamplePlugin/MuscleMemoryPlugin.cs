using BepInEx;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace MuscleMemory
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public sealed class MuscleMemoryPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "AuthorName";
        public const string PluginName = "MuscleMemory";
        public const string PluginVersion = "2.0.0";

        private MuscleMemoryConfig _config;
        private MilestoneSystem _milestones;
        private ProgressionManager _progression;
        private StatHooks _statHooks;
        private NetworkSync _networkSync;
        private SkillHud _skillHud;

        private Run _trackedRun;
        private bool _lateJoinHooked;

        private void Awake()
        {
            Log.Init(Logger);

            _config = new MuscleMemoryConfig();
            _config.Bind(Config);

            _milestones = new MilestoneSystem(_config);
            _progression = new ProgressionManager(_config, _milestones);
            _networkSync = new NetworkSync(_config, _progression);
            _statHooks = new StatHooks(_config, _progression, _milestones, _networkSync);
            _skillHud = new SkillHud(_config, _progression);

            _statHooks.Register();
            _networkSync.TryRegisterClientMessageHandler();
            HookLateJoin();

            Log.Info("Muscle Memory v" + PluginVersion + " initialized.");
        }

        private void OnDestroy()
        {
            _statHooks.Unregister();
            _networkSync.UnregisterClientMessageHandler();
            _networkSync.UnregisterServerMessageHandler();
            UnhookLateJoin();
        }

        private void FixedUpdate()
        {
            _networkSync.TryRegisterClientMessageHandler();
            _networkSync.TryRegisterServerMessageHandler();

            if (_trackedRun != Run.instance)
            {
                HandleRunTransition(Run.instance);
            }

            if (!NetworkServer.active || Run.instance == null)
            {
                return;
            }

            float now = Time.fixedTime;
            _progression.ServerTick(now);
            _networkSync.TryBroadcast(now);
        }

        private void OnGUI()
        {
            _skillHud.DrawHud();
        }

        private void HandleRunTransition(Run nextRun)
        {
            _trackedRun = nextRun;
            _progression.HandleRunTransition();
            _networkSync.ResetReplicationTimer();
        }

        private void HookLateJoin()
        {
            if (_lateJoinHooked)
            {
                return;
            }

            On.RoR2.NetworkUser.Start += NetworkUser_Start;
            _lateJoinHooked = true;
        }

        private void UnhookLateJoin()
        {
            if (!_lateJoinHooked)
            {
                return;
            }

            On.RoR2.NetworkUser.Start -= NetworkUser_Start;
            _lateJoinHooked = false;
        }

        private void NetworkUser_Start(On.RoR2.NetworkUser.orig_Start orig, NetworkUser self)
        {
            orig(self);

            if (!NetworkServer.active || self == null)
            {
                return;
            }

            NetworkConnection conn = self.connectionToClient;
            if (conn != null)
            {
                _networkSync.SendFullSnapshotToClient(conn);
            }
        }
    }
}
