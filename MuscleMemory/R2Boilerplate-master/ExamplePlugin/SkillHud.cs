using System;
using System.Collections.Generic;
using System.Reflection;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace MuscleMemory
{
    internal sealed class SkillHud
    {
        private readonly MuscleMemoryConfig _config;
        private readonly ProgressionManager _progression;

        private GUIStyle _cachedStyle;
        private float _cachedScale = -1f;
        private readonly List<Rect> _visibleIconRectsBuffer = new List<Rect>(8);

        private static FieldInfo _cachedTargetSkillField;
        private static PropertyInfo _cachedTargetSkillProperty;
        private static bool _targetSkillReflectionResolved;

        // Color tiers: Gray (0), White (1-4), Green (5-9), Blue (10-14), Purple (15+)
        private static readonly Color ColorTier0 = new Color(0.6f, 0.6f, 0.6f, 1f);
        private static readonly Color ColorTier1 = Color.white;
        private static readonly Color ColorTier2 = new Color(0.3f, 1f, 0.3f, 1f);
        private static readonly Color ColorTier3 = new Color(0.4f, 0.6f, 1f, 1f);
        private static readonly Color ColorTier4 = new Color(0.8f, 0.4f, 1f, 1f);

        internal SkillHud(MuscleMemoryConfig config, ProgressionManager progression)
        {
            _config = config;
            _progression = progression;
        }

        internal void DrawHud()
        {
            if (!_config.ShowSkillHud.Value || Run.instance == null)
            {
                return;
            }

            CharacterBody localBody = TryGetLocalBody();
            if (localBody == null)
            {
                return;
            }

            if (!_progression.TryGetEffectiveLevels(localBody, out int primary, out int secondary, out int utility, out int special, out _))
            {
                return;
            }

            if (!TryGetSkillIconRects(localBody, out Rect primaryIconRect, out Rect secondaryIconRect, out Rect utilityIconRect, out Rect specialIconRect))
            {
                return;
            }

            float scale = Mathf.Clamp(_config.SkillHudScale.Value, 0.75f, 2f);
            GUIStyle style = GetOrCreateStyle(scale);

            _progression.TryGetLevelProgress(localBody, SkillSlotKind.Primary, out float primaryProgress);
            _progression.TryGetLevelProgress(localBody, SkillSlotKind.Secondary, out float secondaryProgress);
            _progression.TryGetLevelProgress(localBody, SkillSlotKind.Utility, out float utilityProgress);
            _progression.TryGetLevelProgress(localBody, SkillSlotKind.Special, out float specialProgress);

            DrawLevelAboveSkill(primaryIconRect, primary, primaryProgress, scale, style);
            DrawLevelAboveSkill(secondaryIconRect, secondary, secondaryProgress, scale, style);
            DrawLevelAboveSkill(utilityIconRect, utility, utilityProgress, scale, style);
            DrawLevelAboveSkill(specialIconRect, special, specialProgress, scale, style);
        }

        private GUIStyle GetOrCreateStyle(float scale)
        {
            if (_cachedStyle != null && Mathf.Approximately(_cachedScale, scale))
            {
                return _cachedStyle;
            }

            _cachedStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(16f * scale)
            };
            _cachedStyle.normal.textColor = Color.white;
            _cachedScale = scale;
            return _cachedStyle;
        }

        private static void DrawLevelAboveSkill(Rect iconRect, int level, float progress, float scale, GUIStyle style)
        {
            float labelHeight = Mathf.Clamp(iconRect.height * 0.5f, 14f * scale, 24f * scale);
            float gapAboveIcon = Mathf.Max(24f * scale, iconRect.height * 0.6f);
            float labelY = Mathf.Max(0f, iconRect.y - labelHeight - gapAboveIcon);
            Rect labelRect = new Rect(iconRect.x, labelY, iconRect.width, labelHeight);

            Color tierColor = GetTierColor(level);
            DrawSkillLevelLabel(labelRect, level.ToString(), style, tierColor);

            // Draw progress bar below the level number
            float barHeight = 3f * scale;
            float barWidth = iconRect.width * 0.7f;
            float barX = iconRect.x + (iconRect.width - barWidth) * 0.5f;
            float barY = labelRect.yMax + 1f * scale;
            Rect barBgRect = new Rect(barX, barY, barWidth, barHeight);
            Rect barFillRect = new Rect(barX, barY, barWidth * Mathf.Clamp01(progress), barHeight);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(barBgRect, Texture2D.whiteTexture);
            GUI.color = new Color(tierColor.r, tierColor.g, tierColor.b, 0.8f);
            GUI.DrawTexture(barFillRect, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static Color GetTierColor(int level)
        {
            if (level <= 0) return ColorTier0;
            if (level <= 4) return ColorTier1;
            if (level <= 9) return ColorTier2;
            if (level <= 14) return ColorTier3;
            return ColorTier4;
        }

        private static void DrawSkillLevelLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            Color previousColor = GUI.color;

            // Shadow
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);

            // Colored text
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = previousColor;
        }

        private bool TryGetSkillIconRects(CharacterBody localBody, out Rect primaryRect, out Rect secondaryRect, out Rect utilityRect, out Rect specialRect)
        {
            primaryRect = default;
            secondaryRect = default;
            utilityRect = default;
            specialRect = default;

            if (localBody == null)
            {
                return false;
            }

            HUD localHud = null;
            for (int i = 0; i < HUD.readOnlyInstanceList.Count; i++)
            {
                HUD candidate = HUD.readOnlyInstanceList[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.targetBodyObject == localBody.gameObject)
                {
                    localHud = candidate;
                    break;
                }
            }

            if (localHud == null)
            {
                return false;
            }

            SkillIcon[] allSkillIcons = localHud.GetComponentsInChildren<SkillIcon>(true);
            if (allSkillIcons == null || allSkillIcons.Length == 0)
            {
                return false;
            }

            SkillLocator skillLocator = localBody.skillLocator;
            bool foundPrimary = false;
            bool foundSecondary = false;
            bool foundUtility = false;
            bool foundSpecial = false;

            _visibleIconRectsBuffer.Clear();
            for (int i = 0; i < allSkillIcons.Length; i++)
            {
                SkillIcon icon = allSkillIcons[i];
                if (icon == null || !icon.gameObject.activeInHierarchy)
                {
                    continue;
                }

                RectTransform rectTransform = icon.transform as RectTransform;
                if (!TryConvertRectTransformToScreenRect(rectTransform, out Rect iconRect))
                {
                    continue;
                }

                if (iconRect.width < 18f || iconRect.height < 18f)
                {
                    continue;
                }

                _visibleIconRectsBuffer.Add(iconRect);

                GenericSkill targetSkill = TryGetIconTargetSkill(icon);
                if (targetSkill == null || skillLocator == null)
                {
                    continue;
                }

                if (!foundPrimary && targetSkill == skillLocator.primary)
                {
                    primaryRect = iconRect;
                    foundPrimary = true;
                    continue;
                }

                if (!foundSecondary && targetSkill == skillLocator.secondary)
                {
                    secondaryRect = iconRect;
                    foundSecondary = true;
                    continue;
                }

                if (!foundUtility && targetSkill == skillLocator.utility)
                {
                    utilityRect = iconRect;
                    foundUtility = true;
                    continue;
                }

                if (!foundSpecial && targetSkill == skillLocator.special)
                {
                    specialRect = iconRect;
                    foundSpecial = true;
                }
            }

            if (foundPrimary && foundSecondary && foundUtility && foundSpecial)
            {
                return true;
            }

            if (_visibleIconRectsBuffer.Count < Constants.SlotCount)
            {
                return false;
            }

            _visibleIconRectsBuffer.Sort((a, b) => a.x.CompareTo(b.x));
            int startIndex = _visibleIconRectsBuffer.Count - Constants.SlotCount;

            primaryRect = _visibleIconRectsBuffer[startIndex];
            secondaryRect = _visibleIconRectsBuffer[startIndex + 1];
            utilityRect = _visibleIconRectsBuffer[startIndex + 2];
            specialRect = _visibleIconRectsBuffer[startIndex + 3];
            return true;
        }

        private static GenericSkill TryGetIconTargetSkill(SkillIcon icon)
        {
            if (icon == null)
            {
                return null;
            }

            if (!_targetSkillReflectionResolved)
            {
                Type iconType = typeof(SkillIcon);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _cachedTargetSkillField = iconType.GetField("targetSkill", flags);
                if (_cachedTargetSkillField == null)
                {
                    _cachedTargetSkillProperty = iconType.GetProperty("targetSkill", flags);
                }

                _targetSkillReflectionResolved = true;
            }

            if (_cachedTargetSkillField != null)
            {
                return _cachedTargetSkillField.GetValue(icon) as GenericSkill;
            }

            if (_cachedTargetSkillProperty != null)
            {
                return _cachedTargetSkillProperty.GetValue(icon, null) as GenericSkill;
            }

            return null;
        }

        private static bool TryConvertRectTransformToScreenRect(RectTransform rectTransform, out Rect screenRect)
        {
            screenRect = default;
            if (rectTransform == null)
            {
                return false;
            }

            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            Canvas parentCanvas = rectTransform.GetComponentInParent<Canvas>();
            Camera uiCamera = parentCanvas != null ? parentCanvas.worldCamera : null;

            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);

            float width = topRight.x - bottomLeft.x;
            float height = topRight.y - bottomLeft.y;
            if (width <= 0f || height <= 0f)
            {
                return false;
            }

            screenRect = new Rect(bottomLeft.x, Screen.height - topRight.y, width, height);
            return true;
        }

        private static CharacterBody TryGetLocalBody()
        {
            LocalUser localUser = LocalUserManager.GetFirstLocalUser();
            if (localUser == null)
            {
                return null;
            }

            if (localUser.cachedBody != null)
            {
                return localUser.cachedBody;
            }

            if (localUser.cachedMaster != null)
            {
                return localUser.cachedMaster.GetBody();
            }

            return null;
        }
    }
}
