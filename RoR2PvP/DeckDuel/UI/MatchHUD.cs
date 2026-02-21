using System;
using DeckDuel.Match;
using DeckDuel.Models;
using RoR2;
using UnityEngine;
using UnityEngine.UI;

namespace DeckDuel.UI
{
    public class MatchHUD : IDisposable
    {
        private GameObject _canvasObj;
        private Canvas _canvas;

        // HUD Elements
        private Text _timerText;
        private Text _phaseText;
        private Text _scoreText;
        private Text _stocksText;
        private Text _cardsRemainingText;
        private Image _phaseFlash;

        // State
        private MatchPhase _currentPhase = MatchPhase.Lobby;
        private float _displayTimer;
        private int[] _scores = System.Array.Empty<int>();
        private int[] _stocks = System.Array.Empty<int>();
        private float _flashAlpha;
        private bool _isFlashing;

        public MatchHUD()
        {
            On.RoR2.UI.HUD.Awake += HUD_Awake;
            Run.onRunDestroyGlobal += OnRunDestroy;
        }

        public void Hide()
        {
            if (_canvasObj != null)
                _canvasObj.SetActive(false);
        }

        public void Show()
        {
            if (_canvasObj != null)
                _canvasObj.SetActive(true);
        }

        private void OnRunDestroy(Run run)
        {
            Hide();
            _currentPhase = MatchPhase.Lobby;
        }

        private void HUD_Awake(On.RoR2.UI.HUD.orig_Awake orig, RoR2.UI.HUD self)
        {
            orig(self);
            CreateHUD();
        }

        public void UpdateState(MatchPhase phase, int gameNumber, float timer)
        {
            _currentPhase = phase;
            _displayTimer = timer;

            // Hide HUD during non-game phases
            if (phase == MatchPhase.Lobby || phase == MatchPhase.DeckBuilding)
            {
                Hide();
                return;
            }
            else
            {
                Show();
            }

            if (_phaseText != null)
            {
                switch (phase)
                {
                    case MatchPhase.Warmup:
                        _phaseText.text = "GET READY";
                        _phaseText.color = new Color(0.5f, 0.8f, 1f);
                        break;
                    case MatchPhase.Active:
                        _phaseText.text = $"GAME {gameNumber} — FIGHT";
                        _phaseText.color = Color.white;
                        break;
                    case MatchPhase.Tiebreak:
                        _phaseText.text = "TIEBREAK";
                        _phaseText.color = new Color(1f, 0.3f, 0.3f);
                        TriggerPhaseFlash(new Color(1f, 0.5f, 0f, 0.5f));
                        break;
                    case MatchPhase.RoundEnd:
                        _phaseText.text = "GAME OVER";
                        _phaseText.color = new Color(1f, 0.85f, 0.2f);
                        break;
                    case MatchPhase.MatchEnd:
                        int bestIdx = -1; int bestScore = -1;
                        for (int i = 0; i < _scores.Length; i++)
                        { if (_scores[i] > bestScore) { bestScore = _scores[i]; bestIdx = i; } }
                        string winner = bestIdx >= 0 ? $"PLAYER {bestIdx + 1} WINS!" : "DRAW!";
                        _phaseText.text = $"MATCH OVER — {winner}";
                        _phaseText.color = new Color(1f, 0.85f, 0.2f);
                        break;
                    default:
                        _phaseText.text = "";
                        break;
                }
            }

            if (_timerText != null)
            {
                if (phase == MatchPhase.Warmup || phase == MatchPhase.Active || phase == MatchPhase.Tiebreak)
                {
                    int seconds = Mathf.CeilToInt(timer);
                    int mins = seconds / 60;
                    int secs = seconds % 60;
                    _timerText.text = $"{mins}:{secs:D2}";

                    // Color timer red when low
                    if (timer <= 10f)
                        _timerText.color = new Color(1f, 0.3f, 0.3f);
                    else if (timer <= 30f)
                        _timerText.color = new Color(1f, 0.7f, 0.3f);
                    else
                        _timerText.color = Color.white;
                }
                else
                {
                    _timerText.text = "";
                }
            }

            UpdateFlash();
        }

        public void Tick(float deltaTime)
        {
            if (_currentPhase != MatchPhase.Active && _currentPhase != MatchPhase.Tiebreak)
                return;

            _displayTimer = Mathf.Max(0f, _displayTimer - deltaTime);

            if (_timerText != null)
            {
                int seconds = Mathf.CeilToInt(_displayTimer);
                int mins = seconds / 60;
                int secs = seconds % 60;
                _timerText.text = $"{mins}:{secs:D2}";

                if (_displayTimer <= 10f)
                    _timerText.color = new Color(1f, 0.3f, 0.3f);
                else if (_displayTimer <= 30f)
                    _timerText.color = new Color(1f, 0.7f, 0.3f);
                else
                    _timerText.color = Color.white;
            }
        }

        public void UpdateScores(int[] scores)
        {
            _scores = scores ?? System.Array.Empty<int>();
            if (_scoreText != null)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < _scores.Length; i++)
                {
                    if (i > 0) sb.Append(" — ");
                    sb.Append(_scores[i]);
                }
                _scoreText.text = sb.ToString();
            }
        }

        public void UpdateStocks(int[] stocks)
        {
            _stocks = stocks ?? System.Array.Empty<int>();
            if (_stocksText != null)
            {
                var sb = new System.Text.StringBuilder();
                bool anyLow = false;
                for (int i = 0; i < _stocks.Length; i++)
                {
                    if (i > 0) sb.Append("  ");
                    sb.Append($"P{i + 1}:");
                    if (_stocks[i] <= 0)
                        sb.Append("\u2620"); // skull = eliminated
                    else
                        sb.Append(new string('\u2665', _stocks[i]));
                    if (_stocks[i] <= 1) anyLow = true;
                }
                _stocksText.text = sb.ToString();
                _stocksText.color = anyLow ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.5f, 0.5f);
            }
        }

        public void OnCardDealt(uint playerNetId, DeckCardType cardType, int itemOrEquipIndex)
        {
            // Could add a brief visual indicator here in v2
            // For now, just update cards remaining text
            if (_cardsRemainingText != null)
            {
                // This is a simplified display — in practice we'd track per-local-player
                _cardsRemainingText.text = "Card dealt!";
            }
        }

        private void TriggerPhaseFlash(Color color)
        {
            _isFlashing = true;
            _flashAlpha = color.a;
            if (_phaseFlash != null)
                _phaseFlash.color = color;
        }

        private void UpdateFlash()
        {
            if (!_isFlashing || _phaseFlash == null) return;

            _flashAlpha -= Time.deltaTime * 0.5f;
            if (_flashAlpha <= 0f)
            {
                _flashAlpha = 0f;
                _isFlashing = false;
            }
            var c = _phaseFlash.color;
            _phaseFlash.color = new Color(c.r, c.g, c.b, _flashAlpha);
        }

        private void CreateHUD()
        {
            if (_canvasObj != null) return;

            _canvasObj = new GameObject("DeckDuelMatchHUD");
            _canvas = _canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 90;

            var scaler = _canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _canvasObj.AddComponent<GraphicRaycaster>();

            // Timer — top center
            _timerText = CreateText("TimerText", "",
                new Vector2(0.42f, 0.90f), new Vector2(0.58f, 0.97f),
                36, TextAnchor.MiddleCenter, Color.white);

            // Phase label — below timer
            _phaseText = CreateText("PhaseText", "",
                new Vector2(0.30f, 0.85f), new Vector2(0.70f, 0.91f),
                22, TextAnchor.MiddleCenter, Color.white);

            // Stocks — below phase label (centered)
            _stocksText = CreateText("StocksText", "",
                new Vector2(0.35f, 0.80f), new Vector2(0.65f, 0.86f),
                20, TextAnchor.MiddleCenter, new Color(1f, 0.5f, 0.5f));

            // Score — top right
            _scoreText = CreateText("ScoreText", "0 — 0",
                new Vector2(0.82f, 0.90f), new Vector2(0.98f, 0.97f),
                28, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.2f));

            // Cards remaining — bottom right
            _cardsRemainingText = CreateText("CardsText", "",
                new Vector2(0.80f, 0.02f), new Vector2(0.98f, 0.08f),
                16, TextAnchor.MiddleRight, new Color(0.7f, 0.9f, 1f));

            // Phase flash overlay (fullscreen, starts invisible)
            var flashObj = new GameObject("PhaseFlash");
            flashObj.transform.SetParent(_canvasObj.transform, false);
            var flashRect = flashObj.AddComponent<RectTransform>();
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = Vector2.zero;
            flashRect.offsetMax = Vector2.zero;
            _phaseFlash = flashObj.AddComponent<Image>();
            _phaseFlash.color = new Color(1f, 0f, 0f, 0f);
            _phaseFlash.raycastTarget = false;

            UnityEngine.Object.DontDestroyOnLoad(_canvasObj);
        }

        private Text CreateText(string name, string content,
            Vector2 anchorMin, Vector2 anchorMax, int fontSize, TextAnchor alignment, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(_canvasObj.transform, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var outline = obj.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var text = obj.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;

            return text;
        }

        public void Dispose()
        {
            On.RoR2.UI.HUD.Awake -= HUD_Awake;
            Run.onRunDestroyGlobal -= OnRunDestroy;
            if (_canvasObj != null)
                UnityEngine.Object.Destroy(_canvasObj);
        }
    }
}
