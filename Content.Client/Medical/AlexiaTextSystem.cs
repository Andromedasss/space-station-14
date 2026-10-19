using System.Linq;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared.Medical;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.Medical;

/// <summary>
/// Makes all readable text unreadable for a player with the alexia cognitive symptom.
/// Walks every open window and scrambles the letters of the text controls, so consoles,
/// vending machines, books, signs and any other readable surface are affected, while
/// speech and chat are left alone.
/// </summary>
public sealed class AlexiaTextSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _uiManager = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>
    /// Controls that must stay readable: the chat is heard, not read, and the medical
    /// debug window is a tool for the admin rather than something the character reads.
    /// </summary>
    private static readonly Type[] ExcludedTypes = { typeof(ChatBox), typeof(MedicalDebugWindow) };

    /// <summary>
    /// Namespaces of player tools that stay legible, such as the admin panel, the build
    /// menu and the options menu. These are interfaces for the player, not things the
    /// character reads in the fiction.
    /// </summary>
    private static readonly string[] ExcludedNamespaces =
    {
        "Content.Client.Administration",
        "Content.Client.Options",
        "Content.Client.DebugMon",
        "Content.Client.Construction",
        "Content.Client.Sandbox",
        "Content.Client.Info",
        "Content.Client.CharacterInfo",
        "Content.Client.LateJoin",
        "Content.Client.Eui",
        "Content.Client.Voting",
        "Content.Client.RoundEnd",
        "Content.Client.Guidebook",
        "Content.Client.Commands",
        "Content.Client.Tools",
        "Content.Client.ContextMenu",
    };

    private readonly Dictionary<Control, Tracked> _tracked = new();

    private sealed class Tracked
    {
        /// <summary>Last content that came from outside, used to restore it.</summary>
        public FormattedMessage? Original;

        /// <summary>Plain text of the original, for labels that only hold a string.</summary>
        public string? OriginalText;

        /// <summary>The text currently on screen, used to detect outside changes.</summary>
        public string? Shown;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var root = _uiManager.WindowRoot;
        if (root == null)
            return;

        var alexia = HasAlexia();
        var seen = new HashSet<Control>();

        foreach (var control in CollectTextControls(root))
        {
            seen.Add(control);
            UpdateText(control, alexia);
        }

        // Forget controls that belonged to windows that were closed.
        foreach (var key in _tracked.Keys.ToList())
        {
            if (!seen.Contains(key))
                _tracked.Remove(key);
        }
    }

    private void UpdateText(Control control, bool alexia)
    {
        if (!_tracked.TryGetValue(control, out var entry))
        {
            entry = new Tracked();
            _tracked[control] = entry;
        }

        string current;
        FormattedMessage? original = null;

        switch (control)
        {
            case RichTextLabel rich:
                var message = rich.GetFormattedMessage();
                if (message == null)
                    return;
                current = message.ToString();
                original = message;
                break;
            case Label label:
                string? text;
                try
                {
                    text = label.Text;
                }
                catch (InvalidOperationException)
                {
                    // The label uses the text memory API, leave it alone.
                    return;
                }

                current = text ?? string.Empty;
                break;
            default:
                return;
        }

        if (entry.Shown == null || entry.Shown != current)
        {
            // The content was set from outside, remember it so it can be restored.
            entry.Original = original;
            entry.OriginalText = original == null ? current : null;
            entry.Shown = current;
        }

        if (alexia)
        {
            if (control is RichTextLabel richLabel && entry.Original != null)
            {
                var scrambled = NeurologicalTrauma.AlexicMessage(entry.Original);
                richLabel.SetMessage(scrambled);
                entry.Shown = scrambled.ToString();
            }
            else
            {
                var scrambledText = NeurologicalTrauma.AlexicText(entry.OriginalText ?? current);
                if (control is Label plainLabel)
                    plainLabel.Text = scrambledText;
                entry.Shown = scrambledText;
            }
        }
        else if (entry.Original != null && entry.Shown != entry.Original.ToString())
        {
            ((RichTextLabel) control).SetMessage(entry.Original);
            entry.Shown = entry.Original.ToString();
        }
        else if (entry.OriginalText != null && entry.Shown != entry.OriginalText)
        {
            if (control is Label plainLabel)
                plainLabel.Text = entry.OriginalText;
            entry.Shown = entry.OriginalText;
        }
    }

    private bool HasAlexia()
    {
        if (_players.LocalSession?.AttachedEntity is not { } local)
            return false;

        return _entities.TryGetComponent(local, out PhysiologyComponent? physiology)
            && physiology.CognitiveSymptoms.HasFlag(MedicalCognitiveSymptoms.Alexia);
    }

    private static IEnumerable<Control> CollectTextControls(Control control)
    {
        if (control is RichTextLabel or Label)
        {
            // Chat is heard, not read, so it stays legible.
            if (!IsExcluded(control))
                yield return control;
        }

        foreach (var child in control.Children)
        {
            foreach (var found in CollectTextControls(child))
                yield return found;
        }
    }

    /// <summary>
    /// Tools for the player, such as the admin panel or the options menu, are not things
    /// the character reads, so they stay legible even with alexia.
    /// </summary>
    private static bool IsExcluded(Control control)
    {
        var type = control.GetType();

        foreach (var excluded in ExcludedTypes)
        {
            if (excluded.IsInstanceOfType(control))
                return true;
        }

        var space = type.Namespace;
        if (space == null)
            return false;

        foreach (var excluded in ExcludedNamespaces)
        {
            if (space.StartsWith(excluded, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
