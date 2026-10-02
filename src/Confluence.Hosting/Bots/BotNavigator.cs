using System;
using System.Collections.Generic;
using Confluence.Arena.Navigation;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Hosting.Bots
{
    /// <summary>
    /// Path following for a bot over the arena's navigation graph (<see cref="ArenaNav"/>, built from the public
    /// layout with props as obstacles). When the straight line to the goal is clear for the capsule it walks
    /// straight; otherwise it plans a capsule-clearance route, steers toward the next waypoint, and asks for a jump at
    /// ledges the route climbs. It replans on a fixed interval or when the goal moves. The graph knows only the
    /// initial layout and misses some thin solids (PROGRESS known issue), so a stuck check hops and sidesteps, then
    /// replans.
    /// </summary>
    public sealed class BotNavigator
    {
        private const float WaypointReach = 0.6f;        // m: a waypoint counts as reached inside this horizontal radius
        private const float GoalMovedReplan = 2.0f;      // m
        private const int MinPathReplanTicks = 60;       // a graph search at most once a second per bot
        private const int StuckWindowTicks = 30;         // 0.5 s
        private const float StuckDistance = 0.35f;       // m moved per window while trying to move
        private const int UnstickTicks = 24;             // 0.4 s of sidestep after getting stuck
        private const float JumpTriggerDistance = 1.4f;  // m from a ledge the route climbs
        private const float DirectMaxRise = 0.3f;        // m: a straight walk stays on one level

        private readonly ArenaNav _nav;
        private readonly Func<Vec3, Vec3, bool> _clearWalk;
        private readonly List<Vec3> _points = new List<Vec3>();
        private int _index;
        private bool _direct;
        private Vec3 _plannedGoal;
        private int _plannedTick = int.MinValue;
        private Vec3 _windowStart;
        private int _windowTick = int.MinValue;
        private int _unstickUntil = int.MinValue;
        private Vec2 _unstickDir;

        /// <param name="nav">The arena graph (null: always walk straight).</param>
        /// <param name="clearWalk">Whether the capsule can walk straight from the first point to the second.</param>
        public BotNavigator(ArenaNav nav, Func<Vec3, Vec3, bool> clearWalk)
        {
            _nav = nav;
            _clearWalk = clearWalk;
        }

        /// <summary>Graph searches run so far (diagnostics).</summary>
        public int Searches { get; private set; }

        /// <summary>Times the stuck check fired (diagnostics).</summary>
        public int StuckEvents { get; private set; }

        /// <summary>
        /// The horizontal direction to walk this tick toward <paramref name="goal"/> (zero when arrived), and whether to
        /// jump. <paramref name="sidestep"/> picks the unstick direction when the bot is blocked.
        /// </summary>
        public Vec2 Steer(Vec3 position, Vec3 goal, int tick, int replanTicks, Func<Vec2> sidestep, out bool jump)
        {
            jump = false;

            if (tick < _unstickUntil) return _unstickDir;

            Vec2 here = position.XZ();
            if (Vec2.Distance(here, goal.XZ()) <= WaypointReach) return Vec2.Zero;

            if (CheckStuck(position, tick))
            {
                _unstickDir = sidestep != null ? sidestep() : Vec2.Zero;
                _unstickUntil = tick + UnstickTicks;
                _plannedTick = int.MinValue; // replan after the sidestep
                jump = true;
                return _unstickDir;
            }

            int interval = _direct ? replanTicks : Math.Max(replanTicks, MinPathReplanTicks);
            if (tick - _plannedTick >= interval || Vec3.Distance(goal, _plannedGoal) > GoalMovedReplan) Plan(position, goal, tick);

            Vec3 target = goal;
            if (!_direct)
            {
                while (_index < _points.Count && Vec2.Distance(here, _points[_index].XZ()) <= WaypointReach) _index++;
                if (_index < _points.Count) target = _points[_index];
            }

            Vec2 delta = target.XZ() - here;
            float length = delta.Length();
            if (length < 1e-4f) return Vec2.Zero;

            // A route climbing a ledge higher than the automatic step needs a common jump (D-024).
            if (target.Y - position.Y > GlobalRules.Movement.StepHeight + 0.05f && length <= JumpTriggerDistance) jump = true;
            return delta / length;
        }

        /// <summary>Forgets the current plan.</summary>
        public void Reset()
        {
            _points.Clear();
            _index = 0;
            _direct = false;
            _plannedTick = int.MinValue;
            _windowTick = int.MinValue;
            _unstickUntil = int.MinValue;
        }

        private void Plan(Vec3 position, Vec3 goal, int tick)
        {
            _plannedTick = tick;
            _plannedGoal = goal;
            _points.Clear();
            _index = 0;
            _direct = true; // fallback when no route is found: walk straight and let the stuck check help
            if (_nav == null) return;
            if (Math.Abs(goal.Y - position.Y) <= DirectMaxRise && _clearWalk != null && _clearWalk(position, goal)) return;

            int from = _nav.FindNode(position, 1.2f, 2f);
            int to = _nav.FindNode(goal, 3f, 4f);
            if (from < 0 || to < 0) return;

            // Bounded search: routes far longer than the straight line are not worth following.
            Searches++;
            float bound = Vec3.Distance(position, goal) * 3f + 12f;
            var parents = new int[_nav.NodeCount];
            float[] dist = _nav.Distances(new[] { from }, NavQuery.Capsule, parents, bound);
            if (float.IsPositiveInfinity(dist[to])) return;
            _points.AddRange(_nav.PathPoints(ArenaNav.TracePath(parents, to)));
            _direct = _points.Count == 0;
        }

        private bool CheckStuck(Vec3 position, int tick)
        {
            if (_windowTick == int.MinValue || tick - _windowTick > StuckWindowTicks * 2)
            {
                _windowTick = tick;
                _windowStart = position;
                return false;
            }

            if (tick - _windowTick < StuckWindowTicks) return false;
            bool stuck = Vec2.Distance(position.XZ(), _windowStart.XZ()) < StuckDistance;
            _windowTick = tick;
            _windowStart = position;
            if (stuck) StuckEvents++;
            return stuck;
        }
    }
}
