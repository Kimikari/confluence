using Confluence.Sim.Input;

namespace Confluence.Hosting.Duels
{
    /// <summary>
    /// Produces one player's per-tick commands for the duels they fight: fed from the network or played by a bot. The
    /// host asks once per authoritative step, in a fixed order, so a source never sees another player's input (L72).
    /// </summary>
    public interface IInputSource
    {
        /// <summary>A duel world was created for this player (alcove start, L46).</summary>
        void BeginDuel(DuelSeat seat);

        /// <summary>The command for the next step (tick <c>seat.World.Tick + 1</c>).</summary>
        PlayerInput NextInput(DuelSeat seat);

        /// <summary>The duel ended (report set) or was aborted by a void (report null).</summary>
        void EndDuel(DuelSeat seat, DuelReport report);
    }

    /// <summary>
    /// Input fed by the network layer. The transport orders, dedupes, and paces client inputs (its receive buffer) and
    /// submits the command for each server tick; this source hands the latest one to the simulation and repeats it
    /// until the next arrives. While the player is disconnected it sends neutral input facing the last view direction:
    /// an absent player keeps the slot and can lose normally, and nothing pauses (L55).
    /// </summary>
    public sealed class RemoteInputSource : IInputSource
    {
        private PlayerInput _latest;
        private bool _hasInput;

        public bool Connected { get; set; } = true;

        /// <summary>Inputs handed to the simulation since the source was created.</summary>
        public int Consumed { get; private set; }

        public void Submit(in PlayerInput input)
        {
            _latest = input;
            _hasInput = true;
        }

        public void BeginDuel(DuelSeat seat)
        {
            _hasInput = false; // a previous duel's last command never leaks into the next
            _latest = default;
        }

        public PlayerInput NextInput(DuelSeat seat)
        {
            Consumed++;
            if (Connected && _hasInput) return _latest;
            float yaw = seat?.World != null ? seat.World.Player(seat.Index).Motor.YawDeg : 0f;
            return PlayerInput.Idle(yaw);
        }

        public void EndDuel(DuelSeat seat, DuelReport report)
        {
            _hasInput = false;
        }
    }

    /// <summary>Always neutral input (absent players in tests, placeholders).</summary>
    public sealed class IdleInputSource : IInputSource
    {
        public static readonly IdleInputSource Instance = new IdleInputSource();

        public void BeginDuel(DuelSeat seat)
        {
        }

        public PlayerInput NextInput(DuelSeat seat) => PlayerInput.Idle(seat.World.Player(seat.Index).Motor.YawDeg);

        public void EndDuel(DuelSeat seat, DuelReport report)
        {
        }
    }
}
