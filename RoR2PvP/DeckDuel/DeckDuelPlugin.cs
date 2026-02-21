using BepInEx;
using R2API;
using R2API.Networking;
using RoR2;
using UnityEngine;
using DeckDuel.Config;
using DeckDuel.Match;
using DeckDuel.Networking;
using DeckDuel.UI;

namespace DeckDuel
{
    [BepInDependency(NetworkingAPI.PluginGUID)]
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class DeckDuelPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "DeckDuel";
        public const string PluginName = "DeckDuel";
        public const string PluginVersion = "0.1.0";

        public static DeckDuelPlugin Instance { get; private set; }
        public static DeckDuelConfig Cfg { get; private set; }

        internal PvPSetup PvPSetup { get; private set; }
        internal ArenaController ArenaController { get; private set; }
        internal CardDealer CardDealer { get; private set; }
        internal MatchStateMachine MatchStateMachine { get; private set; }
        internal AIOpponent AIOpponent { get; private set; }
        internal DeckBuilderUI DeckBuilderUI { get; private set; }
        internal MatchHUD MatchHUD { get; private set; }

        public void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            Log.Info($"{PluginName} v{PluginVersion} initializing...");

            // Phase 1: Config
            Cfg = new DeckDuelConfig(Config);

            // Phase 2: Networking
            DeckNetMessages.Register();
            MatchSyncMessages.Register();

            // Phase 4: PvP + Arena
            PvPSetup = new PvPSetup();
            ArenaController = new ArenaController();

            // Phase 5: Card Dealer
            CardDealer = new CardDealer();

            // Phase 5b: AI Opponent
            AIOpponent = new AIOpponent();

            // Phase 3: Match State Machine (wires 4+5)
            MatchStateMachine = new MatchStateMachine();

            // Phase 6: UI
            DeckBuilderUI = new DeckBuilderUI();
            MatchHUD = new MatchHUD();

            // Hook into catalog availability to resolve item indices
            RoR2Application.onLoad += OnGameLoad;

            Log.Info($"{PluginName} initialized.");
        }

        private void OnGameLoad()
        {
            Models.ItemTierCosts.Initialize();
            Models.DroneDatabase.Initialize();
            PvPSetup.Initialize();
            Log.Info("DeckDuel catalogs loaded and ready.");
        }

        private void Update()
        {
            MatchStateMachine?.Tick(Time.deltaTime);
            ArenaController?.Tick(Time.deltaTime);
            MatchHUD?.Tick(Time.deltaTime);

            // F3 toggles deck builder
            if (Input.GetKeyDown(KeyCode.F3))
            {
                DeckBuilderUI?.Toggle();
            }
        }

        private void OnDestroy()
        {
            PvPSetup?.Dispose();
            ArenaController?.Dispose();
            MatchStateMachine?.Dispose();
            AIOpponent?.Dispose();
            DeckBuilderUI?.Dispose();
            MatchHUD?.Dispose();
        }
    }
}
