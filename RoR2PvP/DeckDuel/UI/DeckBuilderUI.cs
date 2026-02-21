using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DeckDuel.Models;
using DeckDuel.Networking;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace DeckDuel.UI
{
    public class DeckBuilderUI : IDisposable
    {
        // === Color Palette (RoR2-inspired dark theme with gold accents) ===
        private static readonly Color COL_BG = new Color(0.05f, 0.05f, 0.08f, 0.97f);
        private static readonly Color COL_PANEL = new Color(0.09f, 0.09f, 0.13f, 0.96f);
        private static readonly Color COL_CARD = new Color(0.14f, 0.14f, 0.19f, 0.96f);
        private static readonly Color COL_CARD_HOVER = new Color(0.20f, 0.20f, 0.27f, 0.96f);
        private static readonly Color COL_CARD_PRESS = new Color(0.26f, 0.26f, 0.34f, 0.96f);
        private static readonly Color COL_BORDER = new Color(0.22f, 0.22f, 0.32f, 0.5f);
        private static readonly Color COL_ACCENT = new Color(1f, 0.78f, 0.18f);
        private static readonly Color COL_TEXT = new Color(0.92f, 0.92f, 0.94f);
        private static readonly Color COL_TEXT_DIM = new Color(0.50f, 0.50f, 0.55f);
        private static readonly Color COL_TAB_ACTIVE = new Color(0.18f, 0.18f, 0.26f, 0.98f);
        private static readonly Color COL_TAB_INACTIVE = new Color(0.08f, 0.08f, 0.12f, 0.85f);

        private GameObject _canvasObj;
        private GameObject _mainPanel;
        private GameObject _borderPanel;
        private Canvas _canvas;
        private Deck _currentDeck = new Deck();
        private bool _isVisible;
        private bool _canToggle;

        // Toggle button (always visible, outside main panel)
        private GameObject _toggleBtnObj;

        // UI Elements
        private Text _budgetText;
        private Transform _deckListContent;
        private Text _deckEmptyText;
        private Text _statusText;
        private Transform _itemGridContent;
        private ScrollRect _itemGridScrollRect;
        private Button _submitButton;
        private Button _clearButton;

        // Budget bar
        private Image _budgetBarFill;

        // Tabs
        private int _currentTab;
        private readonly string[] _tabNames = { "White", "Green", "Red", "Boss", "Lunar", "Void", "Equipment", "Drones" };
        private List<Image> _tabBgImages = new List<Image>();
        private List<GameObject> _tabIndicators = new List<GameObject>();

        // Tooltip
        private GameObject _tooltipObj;
        private Text _tooltipTitle;
        private Text _tooltipDesc;
        private RectTransform _tooltipRect;

        // Deck management
        private InputField _deckNameInput;
        private Transform _savedDeckListContent;
        private string _loadedDeckName = "";

        // Tracking stacks for cost calculation
        private Dictionary<int, int> _itemCopyCounts = new Dictionary<int, int>();

        public DeckBuilderUI()
        {
            On.RoR2.UI.CharacterSelectController.Awake += CharacterSelectController_Awake;
            Run.onRunStartGlobal += OnRunStart_HideBuilder;
        }

        public void Show()
        {
            EnsureUI();
            _canvasObj?.SetActive(true);
            _borderPanel?.SetActive(true);
            _isVisible = true;
            PopulateItemGrid();
            RefreshUI();
            RefreshSavedDeckList();
            UpdateToggleLabel();
        }

        public void Hide()
        {
            _borderPanel?.SetActive(false);
            _isVisible = false;
            UpdateToggleLabel();
        }

        public void Toggle()
        {
            if (!_canToggle) return;
            EnsureUI();
            if (_isVisible) Hide(); else Show();
        }

        private void EnsureUI()
        {
            // Retry creation if it failed partway (e.g. _canvasObj exists but _itemGridContent is null)
            if (_itemGridContent == null)
            {
                if (_canvasObj != null)
                {
                    Log.Warning("EnsureUI: partial UI detected, destroying and recreating...");
                    UnityEngine.Object.Destroy(_canvasObj);
                    _canvasObj = null;
                }
                CreateUI();
            }
        }

        private void UpdateToggleLabel()
        {
            if (_toggleBtnObj == null) return;
            var txt = _toggleBtnObj.GetComponentInChildren<Text>();
            if (txt != null)
                txt.text = _isVisible ? "Hide Deck Builder [F3]" : "Deck Builder [F3]";
        }

        public void OnDeckApproved()
        {
            if (_statusText != null)
                _statusText.text = "DECK APPROVED - Waiting for opponent...";
            if (_submitButton != null)
                _submitButton.interactable = false;
        }

        public void OnDeckRejected(string reason)
        {
            if (_statusText != null)
                _statusText.text = $"DECK REJECTED: {reason}";
            if (_submitButton != null)
                _submitButton.interactable = true;
        }

        private void CharacterSelectController_Awake(On.RoR2.UI.CharacterSelectController.orig_Awake orig, CharacterSelectController self)
        {
            orig(self);

            _canToggle = true;

            // Activate canvas so toggle button is visible, but don't open the full panel
            EnsureUI();
            _canvasObj?.SetActive(true);
            _borderPanel?.SetActive(false);
            _isVisible = false;
            UpdateToggleLabel();

            // Re-enable submit for a fresh lobby
            if (_submitButton != null) _submitButton.interactable = true;
            if (_statusText != null) _statusText.text = "";
        }

        private void OnRunStart_HideBuilder(Run run)
        {
            // Keep _canToggle = true so players can still open the deck builder
            // during the DeckBuilding phase (F3 key). Just hide the panel for now.
            _borderPanel?.SetActive(false);
            _isVisible = false;
            UpdateToggleLabel();
        }

        /// <summary>
        /// Called by MatchStateMachine when the match actually starts (leaving DeckBuilding phase).
        /// Fully hides and locks the deck builder.
        /// </summary>
        public void OnMatchStarted()
        {
            _canToggle = false;
            _borderPanel?.SetActive(false);
            _canvasObj?.SetActive(false);
            _isVisible = false;
        }

        private void CreateUI()
        {
            try
            {
                Log.Info("CreateUI: starting...");

                _canvasObj = new GameObject("DeckDuelBuilderCanvas");
                _canvas = _canvasObj.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 100;

                var scaler = _canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                _canvasObj.AddComponent<GraphicRaycaster>();
                Log.Info("CreateUI: canvas created.");

                // Toggle button (always visible, top-left corner)
                _toggleBtnObj = CreateToggleButton(_canvasObj.transform);
                Log.Info("CreateUI: toggle button created.");

                // Main panel — outer border
                _borderPanel = CreatePanel(_canvasObj.transform, "MainBorder",
                    new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f), COL_BORDER);

                // Main panel — inner background
                _mainPanel = CreatePanel(_borderPanel.transform, "MainPanel",
                    Vector2.zero, Vector2.one, COL_BG);
                var mainRect = _mainPanel.GetComponent<RectTransform>();
                mainRect.offsetMin = new Vector2(2f, 2f);
                mainRect.offsetMax = new Vector2(-2f, -2f);

                // === Header Section ===
                // Title with glow outline
                var titleText = CreateText(_mainPanel.transform, "Title", "DECK DUEL",
                    new Vector2(0.30f, 0.93f), new Vector2(0.70f, 0.98f), 30, TextAnchor.MiddleCenter, COL_ACCENT);
                titleText.fontStyle = FontStyle.Bold;
                var titleOutline = titleText.gameObject.AddComponent<Outline>();
                titleOutline.effectColor = new Color(0.8f, 0.55f, 0f, 0.35f);
                titleOutline.effectDistance = new Vector2(1.5f, -1.5f);

                // Subtitle
                CreateText(_mainPanel.transform, "Subtitle", "BUILD YOUR DECK",
                    new Vector2(0.35f, 0.905f), new Vector2(0.65f, 0.935f), 14, TextAnchor.MiddleCenter, COL_TEXT_DIM);

                // Gold separator line
                CreatePanel(_mainPanel.transform, "Separator",
                    new Vector2(0.10f, 0.898f), new Vector2(0.90f, 0.902f), COL_ACCENT);

                // Budget bar area
                CreateBudgetBar(_mainPanel.transform);

                // Status text
                _statusText = CreateText(_mainPanel.transform, "StatusText", "",
                    new Vector2(0.20f, 0.855f), new Vector2(0.80f, 0.875f), 14, TextAnchor.MiddleCenter,
                    new Color(0.9f, 0.8f, 0.2f));
                Log.Info("CreateUI: header created.");

                // === Deck Management Bar ===
                CreateDeckManagementBar(_mainPanel.transform);

                // === Left Panel: Item Catalog ===
                var leftBorder = CreatePanel(_mainPanel.transform, "LeftBorder",
                    new Vector2(0.015f, 0.04f), new Vector2(0.575f, 0.81f), COL_BORDER);
                var leftPanel = CreatePanel(leftBorder.transform, "LeftPanel",
                    Vector2.zero, Vector2.one, COL_PANEL);
                var leftRect = leftPanel.GetComponent<RectTransform>();
                leftRect.offsetMin = new Vector2(1f, 1f);
                leftRect.offsetMax = new Vector2(-1f, -1f);

                // Left panel header label
                CreateText(leftPanel.transform, "CatalogTitle", "ITEM CATALOG",
                    new Vector2(0.02f, 0.93f), new Vector2(0.30f, 0.99f), 13, TextAnchor.MiddleLeft, COL_TEXT_DIM);

                CreateTabButtons(leftPanel.transform);
                Log.Info("CreateUI: tabs created.");

                CreateItemGrid(leftPanel.transform);
                Log.Info($"CreateUI: item grid created. _itemGridContent={(_itemGridContent != null ? "OK" : "NULL")}");

                // === Right Panel: Current Deck ===
                var rightBorder = CreatePanel(_mainPanel.transform, "RightBorder",
                    new Vector2(0.59f, 0.04f), new Vector2(0.985f, 0.81f), COL_BORDER);
                var rightPanel = CreatePanel(rightBorder.transform, "RightPanel",
                    Vector2.zero, Vector2.one, COL_PANEL);
                var rightRect = rightPanel.GetComponent<RectTransform>();
                rightRect.offsetMin = new Vector2(1f, 1f);
                rightRect.offsetMax = new Vector2(-1f, -1f);

                // Deck header with dark background
                var deckHeaderBg = CreatePanel(rightPanel.transform, "DeckHeaderBg",
                    new Vector2(0f, 0.92f), new Vector2(1f, 1f), new Color(0.06f, 0.06f, 0.09f, 0.9f));
                CreateText(deckHeaderBg.transform, "DeckTitle", "YOUR DECK",
                    new Vector2(0.05f, 0f), new Vector2(0.95f, 1f), 16, TextAnchor.MiddleCenter, COL_ACCENT);

                // Scrollable deck list (clickable entries to remove)
                CreateDeckListScroll(rightPanel.transform);

                _submitButton = CreateStyledButton(rightPanel.transform, "SubmitBtn", "SUBMIT DECK",
                    new Vector2(0.05f, 0.02f), new Vector2(0.48f, 0.11f),
                    new Color(0.15f, 0.55f, 0.25f), new Color(0.20f, 0.65f, 0.32f), OnSubmitDeck);

                _clearButton = CreateStyledButton(rightPanel.transform, "ClearBtn", "CLEAR DECK",
                    new Vector2(0.52f, 0.02f), new Vector2(0.95f, 0.11f),
                    new Color(0.60f, 0.15f, 0.15f), new Color(0.72f, 0.22f, 0.22f), OnClearDeck);

                CreateTooltip(_canvasObj.transform);

                _canvasObj.SetActive(false);
                _isVisible = false;
                UnityEngine.Object.DontDestroyOnLoad(_canvasObj);
                Log.Info("CreateUI: complete!");
            }
            catch (Exception ex)
            {
                Log.Error($"CreateUI FAILED: {ex}");
                // Clean up partial creation so EnsureUI can retry
                if (_canvasObj != null)
                {
                    UnityEngine.Object.Destroy(_canvasObj);
                    _canvasObj = null;
                }
                _itemGridContent = null;
            }
        }

        private GameObject CreateToggleButton(Transform parent)
        {
            var obj = new GameObject("ToggleBtn");
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.005f, 0.93f);
            rect.anchorMax = new Vector2(0.115f, 0.985f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // Gold accent border
            var borderImg = obj.AddComponent<Image>();
            borderImg.color = COL_ACCENT;

            // Inner dark background
            var inner = new GameObject("Inner");
            inner.transform.SetParent(obj.transform, false);
            var innerRect = inner.AddComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(1.5f, 1.5f);
            innerRect.offsetMax = new Vector2(-1.5f, -1.5f);

            var innerImg = inner.AddComponent<Image>();
            innerImg.color = new Color(0.08f, 0.08f, 0.12f, 0.97f);

            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = innerImg;
            btn.onClick.AddListener(() => Toggle());
            var colors = btn.colors;
            colors.normalColor = new Color(0.08f, 0.08f, 0.12f, 0.97f);
            colors.highlightedColor = new Color(0.14f, 0.14f, 0.20f, 0.97f);
            colors.pressedColor = new Color(0.05f, 0.05f, 0.08f, 0.97f);
            btn.colors = colors;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(inner.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObj.AddComponent<Text>();
            text.text = "Deck Builder [F3]";
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 14;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = COL_ACCENT;

            return obj;
        }

        private void CreateTabButtons(Transform parent)
        {
            _tabBgImages.Clear();
            _tabIndicators.Clear();

            float totalWidth = 0.96f;
            float startX = 0.02f;
            float tabWidth = totalWidth / _tabNames.Length;
            float gap = 0.002f;

            for (int i = 0; i < _tabNames.Length; i++)
            {
                int tabIdx = i;
                float x0 = startX + tabWidth * i + gap;
                float x1 = startX + tabWidth * (i + 1) - gap;

                var tabObj = new GameObject($"Tab_{_tabNames[i]}");
                tabObj.transform.SetParent(parent, false);
                var tabRect = tabObj.AddComponent<RectTransform>();
                tabRect.anchorMin = new Vector2(x0, 0.90f);
                tabRect.anchorMax = new Vector2(x1, 0.98f);
                tabRect.offsetMin = Vector2.zero;
                tabRect.offsetMax = Vector2.zero;

                var tabImg = tabObj.AddComponent<Image>();
                tabImg.color = (i == 0) ? COL_TAB_ACTIVE : COL_TAB_INACTIVE;
                _tabBgImages.Add(tabImg);

                var btn = tabObj.AddComponent<Button>();
                btn.targetGraphic = tabImg;
                btn.onClick.AddListener(() => OnTabSelected(tabIdx));

                // Tab label
                var labelObj = new GameObject("Label");
                labelObj.transform.SetParent(tabObj.transform, false);
                var labelRect = labelObj.AddComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                var label = labelObj.AddComponent<Text>();
                label.text = _tabNames[i];
                label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                label.fontSize = 12;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = COL_TEXT;

                // Tier-colored bottom indicator (visible on active tab)
                var indicator = new GameObject("Indicator");
                indicator.transform.SetParent(tabObj.transform, false);
                var indRect = indicator.AddComponent<RectTransform>();
                indRect.anchorMin = new Vector2(0.1f, 0f);
                indRect.anchorMax = new Vector2(0.9f, 0f);
                indRect.pivot = new Vector2(0.5f, 0f);
                indRect.sizeDelta = new Vector2(0f, 3f);
                var indImg = indicator.AddComponent<Image>();
                indImg.color = GetTierColor(i);
                indicator.SetActive(i == 0);
                _tabIndicators.Add(indicator);
            }
        }

        private void CreateItemGrid(Transform parent)
        {
            // Scrollable area for items
            var scrollObj = new GameObject("ItemScroll");
            scrollObj.transform.SetParent(parent, false);

            var scrollRect = scrollObj.AddComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.01f, 0.01f);
            scrollRect.anchorMax = new Vector2(0.99f, 0.88f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;

            scrollObj.AddComponent<Image>().color = Color.clear;

            _itemGridScrollRect = scrollObj.AddComponent<ScrollRect>();
            _itemGridScrollRect.vertical = true;
            _itemGridScrollRect.horizontal = false;
            _itemGridScrollRect.scrollSensitivity = 30f;
            _itemGridScrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollObj.transform, false);
            var vpRect = viewport.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;
            viewport.AddComponent<Image>().color = Color.clear;
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            _itemGridContent = content.transform;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            var layout = content.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(275, 52);
            layout.spacing = new Vector2(6, 4);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.padding = new RectOffset(6, 6, 6, 6);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _itemGridScrollRect.content = contentRect;
            _itemGridScrollRect.viewport = vpRect;
        }

        private void OnTabSelected(int tabIndex)
        {
            _currentTab = tabIndex;
            UpdateTabVisuals();
            PopulateItemGrid();
        }

        private void UpdateTabVisuals()
        {
            for (int i = 0; i < _tabBgImages.Count; i++)
            {
                _tabBgImages[i].color = (i == _currentTab) ? COL_TAB_ACTIVE : COL_TAB_INACTIVE;
                if (i < _tabIndicators.Count)
                    _tabIndicators[i].SetActive(i == _currentTab);
            }
        }

        private void PopulateItemGrid()
        {
            if (_itemGridContent == null)
            {
                Log.Warning("PopulateItemGrid: _itemGridContent is null!");
                return;
            }

            // Clear existing buttons
            for (int i = _itemGridContent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_itemGridContent.GetChild(i).gameObject);
            }

            if (_currentTab == 7)
            {
                PopulateDroneGrid();
                return;
            }

            if (_currentTab == 6)
            {
                PopulateEquipmentGrid();
                return;
            }

            // Item tabs — for Void, match all void sub-tiers
            bool isVoidTab = (_currentTab == 5);
            ItemTier targetTier = GetTierForTab(_currentTab);

            int totalItems = ItemCatalog.itemCount;
            int added = 0;
            int skippedNull = 0, skippedPickable = 0, skippedTier = 0;

            Log.Info($"PopulateItemGrid: tab={_currentTab} targetTier={targetTier} totalItems={totalItems}");

            for (int i = 0; i < totalItems; i++)
            {
                var itemDef = ItemCatalog.GetItemDef((ItemIndex)i);
                if (itemDef == null) { skippedNull++; continue; }
                if (!IsItemPickableForUI(itemDef)) { skippedPickable++; continue; }

                if (isVoidTab)
                {
                    if (itemDef.tier != ItemTier.VoidTier1 &&
                        itemDef.tier != ItemTier.VoidTier2 &&
                        itemDef.tier != ItemTier.VoidTier3 &&
                        itemDef.tier != ItemTier.VoidBoss)
                    { skippedTier++; continue; }
                }
                else
                {
                    if (itemDef.tier != targetTier) { skippedTier++; continue; }
                }

                string displayName = Language.GetString(itemDef.nameToken);
                int baseCost = ItemTierCosts.GetBaseCostForItem(itemDef.itemIndex);

                var itemIdx = itemDef.itemIndex;
                Sprite icon = itemDef.pickupIconSprite;
                string desc = Language.GetString(itemDef.descriptionToken);
                CreateItemButton(_itemGridContent, displayName, baseCost, () => AddItemToDeck(itemIdx), icon, desc);
                added++;
            }

            Log.Info($"PopulateItemGrid: added={added} skippedNull={skippedNull} skippedPickable={skippedPickable} skippedTier={skippedTier}");

            if (added == 0)
            {
                // Show a helpful message
                CreateInfoLabel(_itemGridContent, $"No items found for {_tabNames[_currentTab]} tier.\nItemCatalog.itemCount={totalItems}");
            }
        }

        /// <summary>
        /// More permissive filter than ItemTierCosts.IsItemPickable for UI display.
        /// Skips only items with WorldUnique tag or NoTier (unless they have a valid _itemTierDef).
        /// </summary>
        private bool IsItemPickableForUI(ItemDef itemDef)
        {
            if (itemDef == null) return false;
            if (itemDef.tier == ItemTier.NoTier) return false;
            // Skip items tagged as WorldUnique (e.g. tonic affliction, consumed items)
            if (itemDef.ContainsTag(ItemTag.WorldUnique)) return false;
            if (ItemTierCosts.IsItemBanned(itemDef.itemIndex)) return false;
            return true;
        }

        private void PopulateDroneGrid()
        {
            var drones = DroneDatabase.GetAllDrones();
            foreach (var drone in drones)
            {
                string prefabName = drone.MasterPrefabName;
                CreateItemButton(_itemGridContent, drone.DisplayName, drone.Cost,
                    () => AddDroneToDeck(prefabName), null, drone.DisplayName);
            }
        }

        private void PopulateEquipmentGrid()
        {
            for (int i = 0; i < EquipmentCatalog.equipmentCount; i++)
            {
                var equipDef = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)i);
                if (equipDef == null) continue;
                if (!ItemTierCosts.IsEquipmentPickable(equipDef)) continue;

                string displayName = Language.GetString(equipDef.nameToken);
                int cost = ItemTierCosts.GetBaseCostForEquipment(equipDef.equipmentIndex);

                var equipIdx = equipDef.equipmentIndex;
                Sprite icon = equipDef.pickupIconSprite;
                string desc = Language.GetString(equipDef.descriptionToken);
                CreateItemButton(_itemGridContent, displayName, cost, () => SetEquipment(equipIdx, cost), icon, desc);
            }
        }

        private void CreateItemButton(Transform parent, string name, int cost, Action onClick, Sprite icon = null, string description = "")
        {
            var btnObj = new GameObject($"Item_{name}");
            btnObj.transform.SetParent(parent, false);

            // Card background
            var img = btnObj.AddComponent<Image>();
            img.color = COL_CARD;

            var btn = btnObj.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick());

            var colors = btn.colors;
            colors.normalColor = COL_CARD;
            colors.highlightedColor = COL_CARD_HOVER;
            colors.pressedColor = COL_CARD_PRESS;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            // Tier-colored left accent stripe
            var stripe = new GameObject("Stripe");
            stripe.transform.SetParent(btnObj.transform, false);
            var stripeRect = stripe.AddComponent<RectTransform>();
            stripeRect.anchorMin = new Vector2(0f, 0.08f);
            stripeRect.anchorMax = new Vector2(0f, 0.92f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.anchoredPosition = Vector2.zero;
            stripeRect.sizeDelta = new Vector2(4f, 0f);
            var stripeImg = stripe.AddComponent<Image>();
            stripeImg.color = GetTierColor(_currentTab);
            stripeImg.raycastTarget = false;

            float textLeftOffset = 10f;

            // Item icon
            if (icon != null)
            {
                var iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(btnObj.transform, false);
                var iconRect = iconObj.AddComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(8f, 0f);
                iconRect.sizeDelta = new Vector2(40f, 40f);

                var iconImg = iconObj.AddComponent<Image>();
                iconImg.sprite = icon;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;

                textLeftOffset = 52f;
            }

            // Item name text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(textLeftOffset, 0);
            textRect.offsetMax = new Vector2(-50, 0);

            var text = textObj.AddComponent<Text>();
            text.text = name;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = COL_TEXT;
            text.raycastTarget = false;

            // Cost badge (right-aligned dark pill)
            var badgeObj = new GameObject("CostBadge");
            badgeObj.transform.SetParent(btnObj.transform, false);
            var badgeRect = badgeObj.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1f, 0.5f);
            badgeRect.anchorMax = new Vector2(1f, 0.5f);
            badgeRect.pivot = new Vector2(1f, 0.5f);
            badgeRect.anchoredPosition = new Vector2(-6f, 0f);
            badgeRect.sizeDelta = new Vector2(36f, 22f);
            var badgeBg = badgeObj.AddComponent<Image>();
            badgeBg.color = new Color(0.06f, 0.06f, 0.10f, 0.9f);
            badgeBg.raycastTarget = false;

            var costTextObj = new GameObject("CostText");
            costTextObj.transform.SetParent(badgeObj.transform, false);
            var costTextRect = costTextObj.AddComponent<RectTransform>();
            costTextRect.anchorMin = Vector2.zero;
            costTextRect.anchorMax = Vector2.one;
            costTextRect.offsetMin = Vector2.zero;
            costTextRect.offsetMax = Vector2.zero;
            var costLabel = costTextObj.AddComponent<Text>();
            costLabel.text = cost.ToString();
            costLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            costLabel.fontSize = 13;
            costLabel.fontStyle = FontStyle.Bold;
            costLabel.alignment = TextAnchor.MiddleCenter;
            costLabel.color = COL_ACCENT;
            costLabel.raycastTarget = false;

            // Hover tooltip
            string cleanDesc = StripRoR2Tags(description);
            var trigger = btnObj.AddComponent<EventTrigger>();
            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener((_) => ShowTooltip(name, cleanDesc));
            trigger.triggers.Add(enterEntry);

            var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener((_) => HideTooltip());
            trigger.triggers.Add(exitEntry);

            // Forward scroll events to parent ScrollRect so mouse wheel works over buttons
            if (_itemGridScrollRect != null)
            {
                var scrollEntry = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
                scrollEntry.callback.AddListener((data) => _itemGridScrollRect.OnScroll((PointerEventData)data));
                trigger.triggers.Add(scrollEntry);

                var beginDragEntry = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };
                beginDragEntry.callback.AddListener((data) => _itemGridScrollRect.OnBeginDrag((PointerEventData)data));
                trigger.triggers.Add(beginDragEntry);

                var dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
                dragEntry.callback.AddListener((data) => _itemGridScrollRect.OnDrag((PointerEventData)data));
                trigger.triggers.Add(dragEntry);

                var endDragEntry = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };
                endDragEntry.callback.AddListener((data) => _itemGridScrollRect.OnEndDrag((PointerEventData)data));
                trigger.triggers.Add(endDragEntry);
            }
        }

        private void CreateTooltip(Transform parent)
        {
            _tooltipObj = new GameObject("Tooltip");
            _tooltipObj.transform.SetParent(parent, false);

            _tooltipRect = _tooltipObj.AddComponent<RectTransform>();
            _tooltipRect.pivot = new Vector2(0f, 1f);
            _tooltipRect.sizeDelta = new Vector2(340f, 0f);

            var bg = _tooltipObj.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.04f, 0.07f, 0.98f);
            bg.raycastTarget = false;

            var vlg = _tooltipObj.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 10, 10);
            vlg.spacing = 6f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = _tooltipObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Gold accent line at top
            var accentObj = new GameObject("TooltipAccent");
            accentObj.transform.SetParent(_tooltipObj.transform, false);
            var accentImg = accentObj.AddComponent<Image>();
            accentImg.color = COL_ACCENT;
            accentImg.raycastTarget = false;
            var accentLayout = accentObj.AddComponent<LayoutElement>();
            accentLayout.preferredHeight = 2f;
            accentLayout.flexibleWidth = 1f;

            // Title text
            var titleObj = new GameObject("TooltipTitle");
            titleObj.transform.SetParent(_tooltipObj.transform, false);
            _tooltipTitle = titleObj.AddComponent<Text>();
            _tooltipTitle.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _tooltipTitle.fontSize = 18;
            _tooltipTitle.fontStyle = FontStyle.Bold;
            _tooltipTitle.color = COL_ACCENT;
            _tooltipTitle.raycastTarget = false;
            var titleLayout = titleObj.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 26f;

            // Description text
            var descObj = new GameObject("TooltipDesc");
            descObj.transform.SetParent(_tooltipObj.transform, false);
            _tooltipDesc = descObj.AddComponent<Text>();
            _tooltipDesc.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _tooltipDesc.fontSize = 14;
            _tooltipDesc.color = new Color(0.82f, 0.82f, 0.85f);
            _tooltipDesc.raycastTarget = false;
            _tooltipDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tooltipDesc.verticalOverflow = VerticalWrapMode.Overflow;

            _tooltipObj.SetActive(false);
        }

        private void ShowTooltip(string title, string description)
        {
            if (_tooltipObj == null) return;
            _tooltipTitle.text = title;
            _tooltipDesc.text = string.IsNullOrEmpty(description) ? "" : description;
            _tooltipObj.SetActive(true);
            PositionTooltip();
        }

        private void HideTooltip()
        {
            if (_tooltipObj != null)
                _tooltipObj.SetActive(false);
        }

        private void PositionTooltip()
        {
            if (_tooltipRect == null || _canvas == null) return;
            Vector2 pos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvas.transform as RectTransform, Input.mousePosition, _canvas.worldCamera, out pos);
            // Offset slightly so cursor doesn't cover it
            pos += new Vector2(16f, -16f);
            _tooltipRect.anchoredPosition = pos;
        }

        private void CreateInfoLabel(Transform parent, string message)
        {
            var obj = new GameObject("InfoLabel");
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(600, 60);

            var text = obj.AddComponent<Text>();
            text.text = message;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.yellow;
        }

        // === Deck Manipulation ===

        private void AddItemToDeck(ItemIndex itemIndex)
        {
            var cfg = DeckDuelPlugin.Cfg;
            if (_currentDeck.Cards.Count >= cfg.MaxDeckSize.Value)
            {
                _statusText.text = "Deck is full!";
                return;
            }

            int key = (int)itemIndex;
            if (!_itemCopyCounts.ContainsKey(key)) _itemCopyCounts[key] = 0;
            _itemCopyCounts[key]++;

            int baseCost = ItemTierCosts.GetBaseCostForItem(itemIndex);
            int stackCost = ItemTierCosts.ComputeStackCost(baseCost, _itemCopyCounts[key]);

            var card = new DeckCard(DeckCardType.Item, (int)itemIndex, _itemCopyCounts[key], stackCost);
            _currentDeck.Cards.Add(card);
            _currentDeck.RecalculateTotalCost();

            if (_currentDeck.TotalCost > cfg.Budget.Value)
            {
                // Undo
                _currentDeck.Cards.Remove(card);
                _itemCopyCounts[key]--;
                _currentDeck.RecalculateTotalCost();
                _statusText.text = "Over budget!";
                return;
            }

            _statusText.text = "";
            RefreshUI();
        }

        private void AddDroneToDeck(string dronePrefabName)
        {
            var cfg = DeckDuelPlugin.Cfg;
            if (_currentDeck.Cards.Count >= cfg.MaxDeckSize.Value)
            {
                _statusText.text = "Deck is full!";
                return;
            }

            int key = dronePrefabName.GetHashCode();
            if (!_itemCopyCounts.ContainsKey(key)) _itemCopyCounts[key] = 0;
            _itemCopyCounts[key]++;

            int baseCost = DroneDatabase.GetDroneCostByPrefab(dronePrefabName);
            int stackCost = ItemTierCosts.ComputeStackCost(baseCost, _itemCopyCounts[key]);

            var card = new DeckCard(DeckCardType.Drone, 0, _itemCopyCounts[key], stackCost, dronePrefabName);
            _currentDeck.Cards.Add(card);
            _currentDeck.RecalculateTotalCost();

            if (_currentDeck.TotalCost > cfg.Budget.Value)
            {
                _currentDeck.Cards.Remove(card);
                _itemCopyCounts[key]--;
                _currentDeck.RecalculateTotalCost();
                _statusText.text = "Over budget!";
                return;
            }

            _statusText.text = "";
            RefreshUI();
        }

        private void SetEquipment(EquipmentIndex equipIndex, int cost)
        {
            _currentDeck.EquipmentIndex = (int)equipIndex;
            _currentDeck.EquipmentCost = cost;
            _currentDeck.RecalculateTotalCost();

            if (_currentDeck.TotalCost > DeckDuelPlugin.Cfg.Budget.Value)
            {
                _currentDeck.EquipmentIndex = (int)EquipmentIndex.None;
                _currentDeck.EquipmentCost = 0;
                _currentDeck.RecalculateTotalCost();
                _statusText.text = "Over budget!";
                return;
            }

            _statusText.text = "";
            RefreshUI();
        }

        private void OnClearDeck()
        {
            _currentDeck = new Deck();
            _itemCopyCounts.Clear();
            _statusText.text = "";
            RefreshUI();
        }

        private void OnSubmitDeck()
        {
            var result = DeckValidator.Validate(_currentDeck);
            if (!result.IsValid)
            {
                _statusText.text = $"Invalid: {result.Reason}";
                return;
            }

            // Send to host
            if (NetworkServer.active)
            {
                // We are the host — serialize+deserialize to create a safe copy
                // (avoids sharing the mutable _currentDeck reference)
                var deckCopy = Deck.Deserialize(_currentDeck.Serialize());
                DeckDuelPlugin.Instance.MatchStateMachine.OnDeckReceived(deckCopy);
                _statusText.text = "Deck submitted (host).";
                _submitButton.interactable = false;
            }
            else
            {
                new DeckSubmitMessage(_currentDeck).Send(NetworkDestination.Server);
                _statusText.text = "Deck submitted — awaiting approval...";
                _submitButton.interactable = false;
            }
        }

        private void RefreshUI()
        {
            int budget = DeckDuelPlugin.Cfg.Budget.Value;
            int used = _currentDeck.TotalCost;
            float ratio = budget > 0 ? Mathf.Clamp01((float)used / budget) : 0f;

            if (_budgetText != null)
            {
                _budgetText.text = $"Budget: {used} / {budget}  |  Cards: {_currentDeck.Cards.Count} / {DeckDuelPlugin.Cfg.MaxDeckSize.Value}";
            }

            // Update budget bar fill width and color
            if (_budgetBarFill != null)
            {
                var fillRect = _budgetBarFill.rectTransform;
                fillRect.anchorMax = new Vector2(ratio, 1f);
                if (ratio < 0.6f)
                    _budgetBarFill.color = new Color(0.2f, 0.65f, 0.3f, 0.9f);
                else if (ratio < 0.85f)
                    _budgetBarFill.color = new Color(0.8f, 0.7f, 0.15f, 0.9f);
                else
                    _budgetBarFill.color = new Color(0.75f, 0.2f, 0.15f, 0.9f);
            }

            RefreshDeckList();
        }

        private void CreateDeckListScroll(Transform parent)
        {
            // Scrollable area for deck entries
            var scrollObj = new GameObject("DeckListScroll");
            scrollObj.transform.SetParent(parent, false);

            var scrollRectT = scrollObj.AddComponent<RectTransform>();
            scrollRectT.anchorMin = new Vector2(0.02f, 0.14f);
            scrollRectT.anchorMax = new Vector2(0.98f, 0.91f);
            scrollRectT.offsetMin = Vector2.zero;
            scrollRectT.offsetMax = Vector2.zero;

            scrollObj.AddComponent<Image>().color = Color.clear;

            var scrollView = scrollObj.AddComponent<ScrollRect>();
            scrollView.vertical = true;
            scrollView.horizontal = false;
            scrollView.scrollSensitivity = 25f;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollObj.transform, false);
            var vpRect = viewport.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;
            viewport.AddComponent<Image>().color = Color.clear;
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            _deckListContent = content.transform;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.padding = new RectOffset(2, 2, 2, 2);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollView.content = contentRect;
            scrollView.viewport = vpRect;

            // Empty deck text (shown when no cards)
            _deckEmptyText = CreateText(parent, "DeckEmptyText", "<color=#666666>(empty)\n\nClick items on the left to add them.</color>",
                new Vector2(0.04f, 0.40f), new Vector2(0.96f, 0.70f), 14, TextAnchor.MiddleCenter, COL_TEXT_DIM);
            _deckEmptyText.supportRichText = true;
        }

        private void RefreshDeckList()
        {
            if (_deckListContent == null) return;

            // Clear existing entries
            for (int i = _deckListContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_deckListContent.GetChild(i).gameObject);

            bool hasCards = _currentDeck.Cards.Count > 0;
            bool hasEquip = (EquipmentIndex)_currentDeck.EquipmentIndex != EquipmentIndex.None;
            bool isEmpty = !hasCards && !hasEquip;

            if (_deckEmptyText != null)
                _deckEmptyText.gameObject.SetActive(isEmpty);

            if (isEmpty) return;

            // Card entries
            for (int i = 0; i < _currentDeck.Cards.Count; i++)
            {
                var card = _currentDeck.Cards[i];
                string tierCol = GetTierHexColor(card);
                string label = $"<color={tierCol}>\u25A0</color>  {card.GetDisplayName()}  <color=#888888>x{card.CopyNumber}</color>";
                string costStr = $"{card.ResolvedCost}";
                int cardIndex = i;
                CreateDeckEntry(_deckListContent, label, costStr, () => RemoveCardAt(cardIndex));
            }

            // Equipment entry
            if (hasEquip)
            {
                var equipDef = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)_currentDeck.EquipmentIndex);
                string eName = equipDef != null ? Language.GetString(equipDef.nameToken) : "Unknown";
                string label = $"<color=#CC8820>\u25C6</color>  {eName}";
                string costStr = $"{_currentDeck.EquipmentCost}";
                CreateDeckEntry(_deckListContent, label, costStr, RemoveEquipment);
            }
        }

        private void CreateDeckEntry(Transform parent, string label, string costStr, Action onRemove)
        {
            var entryObj = new GameObject("DeckEntry");
            entryObj.transform.SetParent(parent, false);

            var layout = entryObj.AddComponent<LayoutElement>();
            layout.preferredHeight = 28f;

            var bgImg = entryObj.AddComponent<Image>();
            bgImg.color = COL_CARD;

            // Clickable — clicking the entry removes it
            var btn = entryObj.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            btn.onClick.AddListener(() => onRemove());
            var colors = btn.colors;
            colors.normalColor = COL_CARD;
            colors.highlightedColor = new Color(0.35f, 0.12f, 0.12f, 0.96f);
            colors.pressedColor = new Color(0.45f, 0.15f, 0.15f, 0.96f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;

            // Name label (left)
            var nameObj = new GameObject("Name");
            nameObj.transform.SetParent(entryObj.transform, false);
            var nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = new Vector2(6f, 0f);
            nameRect.offsetMax = new Vector2(-60f, 0f);
            var nameText = nameObj.AddComponent<Text>();
            nameText.text = label;
            nameText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            nameText.fontSize = 13;
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.color = COL_TEXT;
            nameText.supportRichText = true;
            nameText.raycastTarget = false;

            // Cost badge (right)
            var costObj = new GameObject("Cost");
            costObj.transform.SetParent(entryObj.transform, false);
            var costRect = costObj.AddComponent<RectTransform>();
            costRect.anchorMin = new Vector2(1f, 0f);
            costRect.anchorMax = new Vector2(1f, 1f);
            costRect.pivot = new Vector2(1f, 0.5f);
            costRect.offsetMin = new Vector2(-52f, 2f);
            costRect.offsetMax = new Vector2(-24f, -2f);
            var costBg = costObj.AddComponent<Image>();
            costBg.color = new Color(0.06f, 0.06f, 0.10f, 0.9f);
            costBg.raycastTarget = false;
            var costLabel = new GameObject("CostText");
            costLabel.transform.SetParent(costObj.transform, false);
            var costLabelRect = costLabel.AddComponent<RectTransform>();
            costLabelRect.anchorMin = Vector2.zero;
            costLabelRect.anchorMax = Vector2.one;
            costLabelRect.offsetMin = Vector2.zero;
            costLabelRect.offsetMax = Vector2.zero;
            var costText = costLabel.AddComponent<Text>();
            costText.text = costStr;
            costText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            costText.fontSize = 12;
            costText.fontStyle = FontStyle.Bold;
            costText.alignment = TextAnchor.MiddleCenter;
            costText.color = COL_ACCENT;
            costText.raycastTarget = false;

            // "X" remove indicator (far right)
            var xObj = new GameObject("RemoveX");
            xObj.transform.SetParent(entryObj.transform, false);
            var xRect = xObj.AddComponent<RectTransform>();
            xRect.anchorMin = new Vector2(1f, 0f);
            xRect.anchorMax = new Vector2(1f, 1f);
            xRect.pivot = new Vector2(1f, 0.5f);
            xRect.offsetMin = new Vector2(-22f, 0f);
            xRect.offsetMax = new Vector2(-4f, 0f);
            var xText = xObj.AddComponent<Text>();
            xText.text = "\u2715";
            xText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            xText.fontSize = 14;
            xText.alignment = TextAnchor.MiddleCenter;
            xText.color = new Color(0.7f, 0.25f, 0.25f);
            xText.raycastTarget = false;
        }

        private void RemoveCardAt(int index)
        {
            if (index < 0 || index >= _currentDeck.Cards.Count) return;

            var card = _currentDeck.Cards[index];

            // Decrement copy count tracking
            int key;
            if (card.CardType == DeckCardType.Drone)
                key = card.DroneMasterPrefabName.GetHashCode();
            else
                key = card.ItemOrEquipIndex;

            if (_itemCopyCounts.ContainsKey(key))
            {
                _itemCopyCounts[key]--;
                if (_itemCopyCounts[key] <= 0)
                    _itemCopyCounts.Remove(key);
            }

            _currentDeck.Cards.RemoveAt(index);
            _currentDeck.RecalculateTotalCost();
            _statusText.text = $"Removed {card.GetDisplayName()}.";
            if (_submitButton != null)
                _submitButton.interactable = true;
            RefreshUI();
        }

        private void RemoveEquipment()
        {
            var equipDef = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)_currentDeck.EquipmentIndex);
            string eName = equipDef != null ? Language.GetString(equipDef.nameToken) : "Equipment";

            _currentDeck.EquipmentIndex = (int)EquipmentIndex.None;
            _currentDeck.EquipmentCost = 0;
            _currentDeck.RecalculateTotalCost();
            _statusText.text = $"Removed {eName}.";
            if (_submitButton != null)
                _submitButton.interactable = true;
            RefreshUI();
        }

        private static string StripRoR2Tags(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            // Remove <style=...> and </style> tags
            string result = Regex.Replace(input, @"<style=[^>]*>", "");
            result = result.Replace("</style>", "");
            // Remove <color=...> and </color> tags too (they may reference game colors)
            result = Regex.Replace(result, @"<color=[^>]*>", "");
            result = result.Replace("</color>", "");
            // Remove any remaining unknown tags like <align=...>
            result = Regex.Replace(result, @"<[a-zA-Z]+=[^>]*>", "");
            result = Regex.Replace(result, @"</[a-zA-Z]+>", "");
            return result.Trim();
        }

        private string GetTierHexColor(DeckCard card)
        {
            if (card.CardType == DeckCardType.Drone) return "#778888";
            if (card.CardType == DeckCardType.Equipment) return "#CC8820";
            var itemDef = ItemCatalog.GetItemDef((ItemIndex)card.ItemOrEquipIndex);
            if (itemDef == null) return "#FFFFFF";
            switch (itemDef.tier)
            {
                case ItemTier.Tier1: return "#CCCCCC";
                case ItemTier.Tier2: return "#44CC44";
                case ItemTier.Tier3: return "#CC4444";
                case ItemTier.Boss: return "#CCCC44";
                case ItemTier.Lunar: return "#5555CC";
                case ItemTier.VoidTier1:
                case ItemTier.VoidTier2:
                case ItemTier.VoidTier3:
                case ItemTier.VoidBoss: return "#AA44CC";
                default: return "#FFFFFF";
            }
        }

        // === UI Helpers ===

        private GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var img = obj.AddComponent<Image>();
            img.color = color;

            return obj;
        }

        private Text CreateText(Transform parent, string name, string content,
            Vector2 anchorMin, Vector2 anchorMax, int fontSize, TextAnchor alignment, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = obj.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;

            return text;
        }

        private Button CreateButton(Transform parent, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax, Color bgColor, Action onClick)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var img = obj.AddComponent<Image>();
            img.color = bgColor;

            var btn = obj.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick());

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObj.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            return btn;
        }

        private ItemTier GetTierForTab(int tab)
        {
            switch (tab)
            {
                case 0: return ItemTier.Tier1;
                case 1: return ItemTier.Tier2;
                case 2: return ItemTier.Tier3;
                case 3: return ItemTier.Boss;
                case 4: return ItemTier.Lunar;
                case 5: return ItemTier.VoidTier1;
                default: return ItemTier.Tier1;
            }
        }

        private Color GetTierColor(int tab)
        {
            switch (tab)
            {
                case 0: return new Color(0.78f, 0.78f, 0.78f);  // White
                case 1: return new Color(0.30f, 0.75f, 0.30f);  // Green
                case 2: return new Color(0.85f, 0.25f, 0.25f);  // Red
                case 3: return new Color(0.85f, 0.80f, 0.20f);  // Yellow/Boss
                case 4: return new Color(0.40f, 0.45f, 0.85f);  // Blue/Lunar
                case 5: return new Color(0.65f, 0.28f, 0.78f);  // Purple/Void
                case 6: return new Color(0.85f, 0.60f, 0.20f);  // Orange/Equipment
                case 7: return new Color(0.45f, 0.62f, 0.60f);  // Teal/Drones
                default: return Color.gray;
            }
        }

        private void CreateBudgetBar(Transform parent)
        {
            // Container
            var container = new GameObject("BudgetArea");
            container.transform.SetParent(parent, false);
            var containerRect = container.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.15f, 0.87f);
            containerRect.anchorMax = new Vector2(0.85f, 0.895f);
            containerRect.offsetMin = Vector2.zero;
            containerRect.offsetMax = Vector2.zero;

            // Budget text label (above bar)
            _budgetText = CreateText(container.transform, "BudgetText", "Budget: 0 / 40",
                new Vector2(0f, 0.5f), new Vector2(1f, 1f), 15, TextAnchor.MiddleCenter, COL_TEXT);

            // Bar background
            var barBg = new GameObject("BarBg");
            barBg.transform.SetParent(container.transform, false);
            var barBgRect = barBg.AddComponent<RectTransform>();
            barBgRect.anchorMin = new Vector2(0f, 0f);
            barBgRect.anchorMax = new Vector2(1f, 0.45f);
            barBgRect.offsetMin = Vector2.zero;
            barBgRect.offsetMax = Vector2.zero;
            var barBgImg = barBg.AddComponent<Image>();
            barBgImg.color = new Color(0.03f, 0.03f, 0.05f, 0.95f);

            // Bar fill
            var barFill = new GameObject("BarFill");
            barFill.transform.SetParent(barBg.transform, false);
            var barFillRect = barFill.AddComponent<RectTransform>();
            barFillRect.anchorMin = Vector2.zero;
            barFillRect.anchorMax = new Vector2(0f, 1f);
            barFillRect.offsetMin = new Vector2(1f, 1f);
            barFillRect.offsetMax = new Vector2(0f, -1f);
            _budgetBarFill = barFill.AddComponent<Image>();
            _budgetBarFill.color = new Color(0.2f, 0.65f, 0.3f, 0.9f);
        }

        private Button CreateStyledButton(Transform parent, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax, Color normalColor, Color hoverColor, Action onClick)
        {
            // Outer border glow
            var borderObj = new GameObject(name + "_Border");
            borderObj.transform.SetParent(parent, false);
            var borderRect = borderObj.AddComponent<RectTransform>();
            borderRect.anchorMin = anchorMin;
            borderRect.anchorMax = anchorMax;
            borderRect.offsetMin = Vector2.zero;
            borderRect.offsetMax = Vector2.zero;
            var borderImg = borderObj.AddComponent<Image>();
            borderImg.color = new Color(
                Mathf.Min(normalColor.r + 0.18f, 1f),
                Mathf.Min(normalColor.g + 0.18f, 1f),
                Mathf.Min(normalColor.b + 0.18f, 1f), 0.5f);

            // Inner button
            var obj = new GameObject(name);
            obj.transform.SetParent(borderObj.transform, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(1.5f, 1.5f);
            rect.offsetMax = new Vector2(-1.5f, -1.5f);

            var img = obj.AddComponent<Image>();
            img.color = normalColor;

            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var colors = btn.colors;
            colors.normalColor = normalColor;
            colors.highlightedColor = hoverColor;
            colors.pressedColor = new Color(normalColor.r * 0.75f, normalColor.g * 0.75f, normalColor.b * 0.75f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObj.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 15;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            return btn;
        }

        // === Deck Management ===

        private void CreateDeckManagementBar(Transform parent)
        {
            // Bordered container
            var barBorder = CreatePanel(parent, "DeckMgmtBorder",
                new Vector2(0.015f, 0.815f), new Vector2(0.985f, 0.85f), COL_BORDER);
            var bar = CreatePanel(barBorder.transform, "DeckMgmtBar",
                Vector2.zero, Vector2.one, COL_PANEL);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.offsetMin = new Vector2(1f, 1f);
            barRect.offsetMax = new Vector2(-1f, -1f);

            // "Deck:" label
            CreateText(bar.transform, "DeckLabel", "DECK:",
                new Vector2(0.01f, 0f), new Vector2(0.06f, 1f), 12, TextAnchor.MiddleRight, COL_TEXT_DIM);

            // Deck name input field
            _deckNameInput = CreateInputField(bar.transform, "DeckNameInput", "Enter deck name...",
                new Vector2(0.065f, 0.12f), new Vector2(0.28f, 0.88f));

            // SAVE button
            CreateStyledButton(bar.transform, "SaveDeckBtn", "SAVE",
                new Vector2(0.29f, 0.08f), new Vector2(0.38f, 0.92f),
                new Color(0.18f, 0.50f, 0.28f), new Color(0.24f, 0.60f, 0.35f), OnSaveDeck);

            // DELETE button
            CreateStyledButton(bar.transform, "DeleteDeckBtn", "DELETE",
                new Vector2(0.39f, 0.08f), new Vector2(0.49f, 0.92f),
                new Color(0.55f, 0.15f, 0.15f), new Color(0.68f, 0.22f, 0.22f), OnDeleteDeck);

            // Separator line
            CreatePanel(bar.transform, "MgmtSep",
                new Vector2(0.502f, 0.15f), new Vector2(0.505f, 0.85f), COL_BORDER);

            // "Saved:" label
            CreateText(bar.transform, "SavedLabel", "SAVED:",
                new Vector2(0.51f, 0f), new Vector2(0.575f, 1f), 12, TextAnchor.MiddleRight, COL_TEXT_DIM);

            // Scrollable saved deck list (horizontal)
            CreateSavedDeckList(bar.transform);
        }

        private void CreateSavedDeckList(Transform parent)
        {
            var scrollObj = new GameObject("SavedDeckScroll");
            scrollObj.transform.SetParent(parent, false);
            var scrollRectTransform = scrollObj.AddComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0.58f, 0.08f);
            scrollRectTransform.anchorMax = new Vector2(0.99f, 0.92f);
            scrollRectTransform.offsetMin = Vector2.zero;
            scrollRectTransform.offsetMax = Vector2.zero;

            var scrollView = scrollObj.AddComponent<ScrollRect>();
            scrollView.horizontal = true;
            scrollView.vertical = false;
            scrollView.scrollSensitivity = 20f;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollObj.transform, false);
            var vpRect = viewport.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;
            viewport.AddComponent<Image>().color = Color.clear;
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            _savedDeckListContent = content.transform;
            contentRect.anchorMin = new Vector2(0, 0);
            contentRect.anchorMax = new Vector2(0, 1);
            contentRect.pivot = new Vector2(0f, 0.5f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            var hlg = content.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset(2, 2, 2, 2);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollView.content = contentRect;
            scrollView.viewport = vpRect;
        }

        private void RefreshSavedDeckList()
        {
            if (_savedDeckListContent == null) return;

            // Clear existing entries
            for (int i = _savedDeckListContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_savedDeckListContent.GetChild(i).gameObject);

            var names = DeckStorage.GetSavedDeckNames();
            foreach (var deckName in names)
            {
                CreateSavedDeckEntry(_savedDeckListContent, deckName);
            }
        }

        private void CreateSavedDeckEntry(Transform parent, string deckName)
        {
            var entryObj = new GameObject($"Deck_{deckName}");
            entryObj.transform.SetParent(parent, false);

            var layout = entryObj.AddComponent<LayoutElement>();
            layout.preferredWidth = 120f;
            layout.minWidth = 80f;

            var bgImg = entryObj.AddComponent<Image>();
            bool isLoaded = deckName.Equals(_loadedDeckName, StringComparison.OrdinalIgnoreCase);
            bgImg.color = isLoaded ? COL_TAB_ACTIVE : COL_CARD;

            var btn = entryObj.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            string capturedName = deckName;
            btn.onClick.AddListener(() => OnLoadDeck(capturedName));

            var colors = btn.colors;
            colors.normalColor = isLoaded ? COL_TAB_ACTIVE : COL_CARD;
            colors.highlightedColor = COL_CARD_HOVER;
            colors.pressedColor = COL_CARD_PRESS;
            colors.fadeDuration = 0.06f;
            btn.colors = colors;

            // Deck name label
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(entryObj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 0f);
            textRect.offsetMax = new Vector2(-6f, 0f);

            var text = textObj.AddComponent<Text>();
            text.text = deckName;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 12;
            text.fontStyle = isLoaded ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = isLoaded ? COL_ACCENT : COL_TEXT;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private InputField CreateInputField(Transform parent, string name, string placeholder,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var bgImg = obj.AddComponent<Image>();
            bgImg.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);

            // Text area with mask
            var textArea = new GameObject("TextArea");
            textArea.transform.SetParent(obj.transform, false);
            var textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(8f, 2f);
            textAreaRect.offsetMax = new Vector2(-8f, -2f);
            textArea.AddComponent<RectMask2D>();

            // Input text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(textArea.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var inputText = textObj.AddComponent<Text>();
            inputText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            inputText.fontSize = 13;
            inputText.color = COL_TEXT;
            inputText.supportRichText = false;
            inputText.alignment = TextAnchor.MiddleLeft;

            // Placeholder text
            var phObj = new GameObject("Placeholder");
            phObj.transform.SetParent(textArea.transform, false);
            var phRect = phObj.AddComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = Vector2.zero;
            phRect.offsetMax = Vector2.zero;

            var phText = phObj.AddComponent<Text>();
            phText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            phText.fontSize = 13;
            phText.fontStyle = FontStyle.Italic;
            phText.color = COL_TEXT_DIM;
            phText.text = placeholder;
            phText.alignment = TextAnchor.MiddleLeft;

            var inputField = obj.AddComponent<InputField>();
            inputField.textComponent = inputText;
            inputField.placeholder = phText;
            inputField.characterLimit = 30;
            inputField.caretColor = COL_ACCENT;
            inputField.selectionColor = new Color(COL_ACCENT.r, COL_ACCENT.g, COL_ACCENT.b, 0.25f);

            return inputField;
        }

        private void OnSaveDeck()
        {
            if (_deckNameInput == null) return;
            string deckName = _deckNameInput.text?.Trim();
            if (string.IsNullOrWhiteSpace(deckName))
            {
                _statusText.text = "Enter a deck name first!";
                return;
            }

            if (_currentDeck.Cards.Count == 0 &&
                (EquipmentIndex)_currentDeck.EquipmentIndex == EquipmentIndex.None)
            {
                _statusText.text = "Cannot save an empty deck.";
                return;
            }

            DeckStorage.SaveDeck(deckName, _currentDeck);
            _loadedDeckName = deckName;
            _statusText.text = $"Deck \"{deckName}\" saved!";
            RefreshSavedDeckList();
        }

        private void OnLoadDeck(string deckName)
        {
            var loaded = DeckStorage.LoadDeck(deckName);
            if (loaded == null)
            {
                _statusText.text = $"Failed to load \"{deckName}\".";
                return;
            }

            _currentDeck = loaded;
            _loadedDeckName = deckName;
            RebuildCopyCounts();

            if (_deckNameInput != null)
                _deckNameInput.text = deckName;

            _statusText.text = $"Loaded \"{deckName}\" ({_currentDeck.Cards.Count} cards).";
            if (_submitButton != null)
                _submitButton.interactable = true;

            RefreshUI();
            RefreshSavedDeckList();
        }

        private void OnDeleteDeck()
        {
            if (_deckNameInput == null) return;
            string deckName = _deckNameInput.text?.Trim();
            if (string.IsNullOrWhiteSpace(deckName))
            {
                _statusText.text = "Enter a deck name to delete.";
                return;
            }

            if (!DeckStorage.DeckExists(deckName))
            {
                _statusText.text = $"No saved deck named \"{deckName}\".";
                return;
            }

            DeckStorage.DeleteDeck(deckName);
            _statusText.text = $"Deleted \"{deckName}\".";

            if (_loadedDeckName.Equals(deckName, StringComparison.OrdinalIgnoreCase))
                _loadedDeckName = "";

            RefreshSavedDeckList();
        }

        private void RebuildCopyCounts()
        {
            _itemCopyCounts.Clear();
            foreach (var card in _currentDeck.Cards)
            {
                int key;
                if (card.CardType == DeckCardType.Drone)
                    key = card.DroneMasterPrefabName.GetHashCode();
                else
                    key = card.ItemOrEquipIndex;

                if (!_itemCopyCounts.ContainsKey(key))
                    _itemCopyCounts[key] = 0;
                _itemCopyCounts[key]++;
            }
        }

        public void Dispose()
        {
            On.RoR2.UI.CharacterSelectController.Awake -= CharacterSelectController_Awake;
            Run.onRunStartGlobal -= OnRunStart_HideBuilder;
            if (_canvasObj != null)
                UnityEngine.Object.Destroy(_canvasObj);
        }
    }
}
