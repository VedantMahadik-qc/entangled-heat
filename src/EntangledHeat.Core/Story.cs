using System;
using System.Collections.Generic;

namespace EntangledHeat.Core
{
    public enum StoryEvent
    {
        FirstWanted,
        Collapse,
        FiveStars,
        IbmRequested,
        IbmArrived,
        Decoherence,
        SearchGrid,
        SafehouseSuperposed,
        SafehouseMissed,
        Escaped,
    }

    /// <summary>
    /// Personality and teaching: phone texts from "Uncle Shor" (Moth's own bounty-page joke) and
    /// one-time help tips. Flavour only: no game decision depends on which line is picked.
    /// </summary>
    public sealed class Story
    {
        public const string Sender = "Uncle Shor";
        static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(15);

        static readonly Dictionary<StoryEvent, string[]> Texts = new Dictionary<StoryEvent, string[]>
        {
            [StoryEvent.FirstWanted] = new[]
            {
                "Kid, the cops are a quantum state now. Their cars come in entangled pairs that share one brain. Break a pair and the other one goes blind.",
                "Heads up: every squad car near you is a qubit. Pairs make the same call. Use that.",
            },
            [StoryEvent.FiveStars] = new[]
            {
                "Five stars. The whole squad is one entangled chain now. When one moves, they all move. Good luck.",
            },
            [StoryEvent.IbmRequested] = new[]
            {
                "That much heat deserves real hardware. I've queued your chase on an IBM quantum computer. The simulator covers you until it's back.",
            },
            [StoryEvent.IbmArrived] = new[]
            {
                "Hardware results are in. Real qubits are deciding who chases you now. They're noisy, so partners won't always agree. Find the gaps.",
            },
            [StoryEvent.Decoherence] = new[]
            {
                "Nice. You decohered one. Its partner just lost you, but not for long.",
                "Entanglement broken. Its partner's driving blind. Move.",
            },
            [StoryEvent.SearchGrid] = new[]
            {
                "They lost sight of you. Now they're sweeping blocks, and the blocks are entangled too. Stay out of the red.",
            },
            [StoryEvent.SafehouseSuperposed] = new[]
            {
                "I've got three safehouses for you. None of them is real until you get there. Pick one.",
                "Safehouse is in superposition. Three places at once. Go collapse it.",
            },
            [StoryEvent.SafehouseMissed] = new[]
            {
                "Wavefunction collapsed the wrong way. The real one's marked. Drive.",
            },
            [StoryEvent.Escaped] = new[]
            {
                "Clean. Heat's gone. Measurement is a beautiful thing.",
                "You're out. Somewhere a physicist is very proud of you.",
            },
        };

        static readonly Dictionary<StoryEvent, string> HelpTips = new Dictionary<StoryEvent, string>
        {
            [StoryEvent.Collapse] = "~p~Escape the quantum police!~s~ Cars with the same ~b~number~s~ are entangled partners: ~r~red~s~ chases you, ~b~blue~s~ cuts you off. Wreck one and its partner loses you.",
            [StoryEvent.Decoherence] = "Wrecking a car ~p~decoheres~s~ its partner: it loses your trail for a while. Pick your targets.",
            [StoryEvent.SearchGrid] = "~r~Red zones~s~ are blocks being searched. ~b~Blue~s~ blips are roadblocks sealing them in.",
            [StoryEvent.SafehouseSuperposed] = "~p~Purple~s~ safehouses are in superposition. The first one you reach is measured.",
        };

        // Once per session; the rest can repeat after the cooldown.
        static readonly HashSet<StoryEvent> Once = new HashSet<StoryEvent>
        {
            StoryEvent.FirstWanted, StoryEvent.FiveStars, StoryEvent.IbmRequested, StoryEvent.IbmArrived, StoryEvent.SearchGrid,
        };

        readonly Random _rng;
        readonly HashSet<StoryEvent> _texted = new HashSet<StoryEvent>();
        readonly HashSet<StoryEvent> _tipped = new HashSet<StoryEvent>();
        DateTime _lastText = DateTime.MinValue;

        public Story(Random rng = null) => _rng = rng ?? new Random();

        /// <summary>A phone text for this event, or null if it was already sent or the phone is cooling down.</summary>
        public string Text(StoryEvent e, DateTime now)
        {
            if (!Texts.TryGetValue(e, out var lines))
                return null;
            if (Once.Contains(e) && _texted.Contains(e))
                return null;
            // Escaping always gets its text; anything else waits out the cooldown.
            if (e != StoryEvent.Escaped && now - _lastText < Cooldown)
                return null;
            _texted.Add(e);
            _lastText = now;
            return lines[_rng.Next(lines.Length)];
        }

        /// <summary>A help tip the first time this event happens in a session, otherwise null.</summary>
        public string Tip(StoryEvent e) =>
            HelpTips.TryGetValue(e, out var tip) && _tipped.Add(e) ? tip : null;
    }
}
