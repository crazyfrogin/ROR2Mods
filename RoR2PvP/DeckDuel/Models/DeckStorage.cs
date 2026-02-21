using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace DeckDuel.Models
{
    public static class DeckStorage
    {
        private static string _savePath;

        private static void EnsurePath()
        {
            if (_savePath != null) return;
            _savePath = Path.Combine(Paths.ConfigPath, "DeckDuel", "Decks");
            if (!Directory.Exists(_savePath))
                Directory.CreateDirectory(_savePath);
        }

        public static void SaveDeck(string name, Deck deck)
        {
            EnsurePath();
            string safeName = SanitizeFileName(name);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                Log.Warning("DeckStorage.SaveDeck: invalid name.");
                return;
            }

            try
            {
                string filePath = Path.Combine(_savePath, safeName + ".deck");
                byte[] data = deck.Serialize();
                File.WriteAllBytes(filePath, data);
                Log.Info($"DeckStorage: saved deck '{safeName}' ({data.Length} bytes).");
            }
            catch (Exception ex)
            {
                Log.Error($"DeckStorage.SaveDeck failed: {ex}");
            }
        }

        public static Deck LoadDeck(string name)
        {
            EnsurePath();
            string safeName = SanitizeFileName(name);
            string filePath = Path.Combine(_savePath, safeName + ".deck");

            if (!File.Exists(filePath))
            {
                Log.Warning($"DeckStorage.LoadDeck: file not found for '{safeName}'.");
                return null;
            }

            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                var deck = Deck.Deserialize(data);
                Log.Info($"DeckStorage: loaded deck '{safeName}' ({deck.Cards.Count} cards).");
                return deck;
            }
            catch (Exception ex)
            {
                Log.Error($"DeckStorage.LoadDeck failed: {ex}");
                return null;
            }
        }

        public static bool DeleteDeck(string name)
        {
            EnsurePath();
            string safeName = SanitizeFileName(name);
            string filePath = Path.Combine(_savePath, safeName + ".deck");

            if (!File.Exists(filePath))
            {
                Log.Warning($"DeckStorage.DeleteDeck: file not found for '{safeName}'.");
                return false;
            }

            try
            {
                File.Delete(filePath);
                Log.Info($"DeckStorage: deleted deck '{safeName}'.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"DeckStorage.DeleteDeck failed: {ex}");
                return false;
            }
        }

        public static List<string> GetSavedDeckNames()
        {
            EnsurePath();
            var names = new List<string>();
            if (!Directory.Exists(_savePath)) return names;

            foreach (var file in Directory.GetFiles(_savePath, "*.deck"))
            {
                names.Add(Path.GetFileNameWithoutExtension(file));
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static bool DeckExists(string name)
        {
            EnsurePath();
            string safeName = SanitizeFileName(name);
            if (string.IsNullOrWhiteSpace(safeName)) return false;
            return File.Exists(Path.Combine(_savePath, safeName + ".deck"));
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
            return sanitized.Trim();
        }
    }
}
