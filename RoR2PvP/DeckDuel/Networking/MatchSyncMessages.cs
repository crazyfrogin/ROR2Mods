using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine.Networking;

namespace DeckDuel.Networking
{
    public static class MatchSyncMessages
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<MatchStateMessage>();
            NetworkingAPI.RegisterMessageType<DealCardMessage>();
            NetworkingAPI.RegisterMessageType<MatchScoreMessage>();
            NetworkingAPI.RegisterMessageType<StockSyncMessage>();
        }
    }

    /// <summary>
    /// Host → All: broadcast current match phase & timer.
    /// </summary>
    public class MatchStateMessage : INetMessage
    {
        private byte _phase;
        private int _roundNumber;
        private float _timer;

        public MatchStateMessage() { }

        public MatchStateMessage(Match.MatchPhase phase, int roundNumber, float timer)
        {
            _phase = (byte)phase;
            _roundNumber = roundNumber;
            _timer = timer;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(_phase);
            writer.Write(_roundNumber);
            writer.Write(_timer);
        }

        public void Deserialize(NetworkReader reader)
        {
            _phase = reader.ReadByte();
            _roundNumber = reader.ReadInt32();
            _timer = reader.ReadSingle();
        }

        public void OnReceived()
        {
            var phase = (Match.MatchPhase)_phase;
            DeckDuelPlugin.Instance.MatchStateMachine.OnMatchStateReceived(phase, _roundNumber, _timer);
            DeckDuelPlugin.Instance.MatchHUD?.UpdateState(phase, _roundNumber, _timer);
        }
    }

    /// <summary>
    /// Host → All: a card was dealt to a player.
    /// </summary>
    public class DealCardMessage : INetMessage
    {
        private uint _playerNetId;
        private byte _cardType;
        private int _itemOrEquipIndex;
        private string _dronePrefab;

        public DealCardMessage() { }

        public DealCardMessage(uint playerNetId, Models.DeckCard card)
        {
            _playerNetId = playerNetId;
            _cardType = (byte)card.CardType;
            _itemOrEquipIndex = card.ItemOrEquipIndex;
            _dronePrefab = card.DroneMasterPrefabName ?? string.Empty;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(_playerNetId);
            writer.Write(_cardType);
            writer.Write(_itemOrEquipIndex);
            writer.Write(_dronePrefab);
        }

        public void Deserialize(NetworkReader reader)
        {
            _playerNetId = reader.ReadUInt32();
            _cardType = reader.ReadByte();
            _itemOrEquipIndex = reader.ReadInt32();
            _dronePrefab = reader.ReadString();
        }

        public void OnReceived()
        {
            // Client-side: used for HUD updates (card dealt animation, cards remaining)
            DeckDuelPlugin.Instance.MatchHUD?.OnCardDealt(_playerNetId, (Models.DeckCardType)_cardType, _itemOrEquipIndex);
        }
    }

    /// <summary>
    /// Host → All: current match score update (N players).
    /// </summary>
    public class MatchScoreMessage : INetMessage
    {
        private int[] _scores;

        public MatchScoreMessage() { }

        public MatchScoreMessage(int[] scores)
        {
            _scores = scores ?? System.Array.Empty<int>();
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write((byte)_scores.Length);
            for (int i = 0; i < _scores.Length; i++)
                writer.Write(_scores[i]);
        }

        public void Deserialize(NetworkReader reader)
        {
            int count = reader.ReadByte();
            _scores = new int[count];
            for (int i = 0; i < count; i++)
                _scores[i] = reader.ReadInt32();
        }

        public void OnReceived()
        {
            DeckDuelPlugin.Instance.MatchHUD?.UpdateScores(_scores);
        }
    }

    /// <summary>
    /// Host → All: current stock counts for all players.
    /// </summary>
    public class StockSyncMessage : INetMessage
    {
        private int[] _stocks;

        public StockSyncMessage() { }

        public StockSyncMessage(int[] stocks)
        {
            _stocks = stocks ?? System.Array.Empty<int>();
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write((byte)_stocks.Length);
            for (int i = 0; i < _stocks.Length; i++)
                writer.Write(_stocks[i]);
        }

        public void Deserialize(NetworkReader reader)
        {
            int count = reader.ReadByte();
            _stocks = new int[count];
            for (int i = 0; i < count; i++)
                _stocks[i] = reader.ReadInt32();
        }

        public void OnReceived()
        {
            DeckDuelPlugin.Instance.MatchHUD?.UpdateStocks(_stocks);
        }
    }
}
