using R2API.Networking;
using R2API.Networking.Interfaces;
using UnityEngine.Networking;

namespace DeckDuel.Networking
{
    public static class DeckNetMessages
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<DeckSubmitMessage>();
            NetworkingAPI.RegisterMessageType<DeckResultMessage>();
        }
    }

    /// <summary>
    /// Client → Host: submit a deck for validation.
    /// </summary>
    public class DeckSubmitMessage : INetMessage
    {
        private byte[] _deckData;

        public DeckSubmitMessage() { }

        public DeckSubmitMessage(Models.Deck deck)
        {
            _deckData = deck.Serialize();
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(_deckData.Length);
            writer.Write(_deckData, 0, _deckData.Length);
        }

        public void Deserialize(NetworkReader reader)
        {
            int length = reader.ReadInt32();
            _deckData = reader.ReadBytes(length);
        }

        public void OnReceived()
        {
            if (!NetworkServer.active)
                return;

            var deck = Models.Deck.Deserialize(_deckData);
            var result = Models.DeckValidator.Validate(deck);

            if (result.IsValid)
            {
                DeckDuelPlugin.Instance.MatchStateMachine.OnDeckReceived(deck);
                Log.Info("Deck received and validated successfully.");
            }
            else
            {
                Log.Warning($"Deck rejected: {result.Reason}");
                // Send rejection back to all clients (in v1, only 2 players)
                new DeckResultMessage(false, result.Reason).Send(NetworkDestination.Clients);
            }
        }
    }

    /// <summary>
    /// Host → Client: deck validation result.
    /// </summary>
    public class DeckResultMessage : INetMessage
    {
        private bool _approved;
        private string _reason;

        public DeckResultMessage() { }

        public DeckResultMessage(bool approved, string reason = "")
        {
            _approved = approved;
            _reason = reason ?? string.Empty;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(_approved);
            writer.Write(_reason);
        }

        public void Deserialize(NetworkReader reader)
        {
            _approved = reader.ReadBoolean();
            _reason = reader.ReadString();
        }

        public void OnReceived()
        {
            if (_approved)
            {
                Log.Info("Deck approved by host.");
                DeckDuelPlugin.Instance.DeckBuilderUI?.OnDeckApproved();
            }
            else
            {
                Log.Warning($"Deck rejected by host: {_reason}");
                DeckDuelPlugin.Instance.DeckBuilderUI?.OnDeckRejected(_reason);
            }
        }
    }
}
