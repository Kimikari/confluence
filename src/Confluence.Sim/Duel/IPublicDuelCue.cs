using Confluence.Core.Geometry;

namespace Confluence.Sim.Duel
{
    /// <summary>
    /// A content event both players perceive (spec L96 cues, L447 counterplay readability, L549 altered cues): a kit's own
    /// world cue such as a bubble breaking, a stream turning or a landing moving. Duel events are private to their player
    /// by default; the network layer replicates an event implementing this interface to the opponent as well, with its
    /// position (subject to the hidden-player concealment of spec L120).
    /// </summary>
    public interface IPublicDuelCue
    {
        /// <summary>The catalogue record the cue belongs to.</summary>
        string CueSourceId { get; }

        /// <summary>The record's own cue kind (0-255).</summary>
        int CueKind { get; }

        Vec3 CuePosition { get; }

        /// <summary>A kind-specific magnitude, or 0.</summary>
        float CueAmount { get; }

        /// <summary>The player the cue concerns, or -1.</summary>
        int CueTarget { get; }
    }
}
