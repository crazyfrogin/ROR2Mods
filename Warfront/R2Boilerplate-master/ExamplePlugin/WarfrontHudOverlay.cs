using RoR2;
using UnityEngine;

namespace WarfrontDirector
{
    internal sealed class WarfrontHudOverlay : MonoBehaviour
    {
        private static readonly Rect PanelRect = new Rect(14f, 132f, 320f, 102f);
        private static readonly Rect IntensityBarRect = new Rect(24f, 176f, 284f, 12f);
        private static readonly Rect SiegeTierBarRect = new Rect(24f, 192f, 284f, 12f);

        private GUIStyle _headerStyle;
        private GUIStyle _textStyle;

        private void OnGUI()
        {
            if (!WarfrontDirectorPlugin.Enabled.Value || Run.instance == null)
            {
                return;
            }

            if (!WarfrontDirectorController.TryGetHudSnapshot(out var snapshot) || !snapshot.Active)
            {
                return;
            }

            EnsureStyles();

            GUI.color = new Color(0f, 0f, 0f, 0.58f);
            GUI.Box(PanelRect, GUIContent.none);

            GUI.color = Color.white;
            var roleText = snapshot.DominantRole == WarfrontRole.None ? "" : $" [{snapshot.DominantRole}]";
            GUI.Label(new Rect(24f, 138f, 284f, 20f), $"Warfront: {snapshot.Phase}{roleText} / {FormatDoctrine(snapshot.Doctrine)}", _headerStyle);
            GUI.Label(new Rect(24f, 154f, 284f, 18f), TrimOperationSummary(snapshot.OperationSummary), _textStyle);

            DrawBar(IntensityBarRect, new Color(0.2f, 0.55f, 0.95f), Mathf.Clamp01(snapshot.Intensity / 100f));
            DrawBar(SiegeTierBarRect, snapshot.ContestColor, Mathf.Clamp01(snapshot.ContestDelta / 3f));

            GUI.color = Color.white;
            GUI.Label(new Rect(24f, 208f, 284f, 16f),
                $"Int {snapshot.Intensity:0} | Chg {snapshot.ChargeFraction * 100f:0}% | Cmd {snapshot.ActiveCommanders}", _textStyle);

            var fairnessText = snapshot.MercyActive
                ? "Mercy"
                : snapshot.LoneWolfPressure > 0.15f
                    ? $"LoneWolf {snapshot.LoneWolfPressure * 100f:0}%"
                    : "Stable";

            GUI.Label(new Rect(24f, 221f, 284f, 16f),
                $"{(snapshot.AssaultActive ? "Assault" : "Breather")} T{snapshot.ContestDelta:0} {snapshot.WindowTimeRemaining:0}s | {fairnessText}", _textStyle);
        }

        private void DrawBar(Rect rect, Color fill, float normalizedValue)
        {
            GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
            GUI.Box(rect, GUIContent.none);

            var filledRect = new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * normalizedValue, rect.height - 2f);
            GUI.color = fill;
            GUI.DrawTexture(filledRect, Texture2D.whiteTexture);
        }

        private void EnsureStyles()
        {
            if (_headerStyle != null)
            {
                return;
            }

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.92f, 0.92f, 0.92f, 1f) }
            };
        }

        private static string TrimOperationSummary(string summary)
        {
            if (string.IsNullOrEmpty(summary))
            {
                return string.Empty;
            }

            const int maxLength = 56;
            return summary.Length <= maxLength
                ? summary
                : summary.Substring(0, maxLength - 3) + "...";
        }

        private static string FormatDoctrine(WarfrontDoctrineProfile doctrine)
        {
            return doctrine switch
            {
                WarfrontDoctrineProfile.Balanced => "Balanced",
                WarfrontDoctrineProfile.SwarmFront => "Swarm",
                WarfrontDoctrineProfile.ArtilleryFront => "Artillery",
                WarfrontDoctrineProfile.HunterCell => "Hunter",
                WarfrontDoctrineProfile.SiegeFront => "Siege",
                WarfrontDoctrineProfile.DisruptionFront => "Disruption",
                _ => doctrine.ToString()
            };
        }
    }
}
