namespace Pleiades.CoreSim
{
    /// <summary>Timed impulse node for planner Layer B (not continuous Layer 1 burn).</summary>
    public struct BurnNode
    {
        /// <summary>Absolute sim time (seconds) when the node becomes eligible.</summary>
        public double T;
        /// <summary>Ship true anomaly at which to fire (Earth-centric).</summary>
        public double TrueAnomalyRad;
        public double DvPrograde;
        public double DvRadial;
        public bool Consumed;
    }

    /// <summary>
    /// Armed Hohmann leave+arrive plan. Same-body circular target at slot.RadiusM;
    /// for moon-distance slots, TargetMoonAnomalyAtArrival marks Moon phase at t_arr
    /// (not SOI intercept).
    /// </summary>
    public sealed class ManeuverPlan
    {
        public BurnNode[] Nodes;
        public double ArrivalSimTime;
        public double TargetMoonAnomalyAtArrival;
        public Hohmann.Transfer Transfer;
        public OrbitSlot Slot;

        public bool AllConsumed
        {
            get
            {
                if (Nodes == null || Nodes.Length == 0) return true;
                for (var i = 0; i < Nodes.Length; i++)
                {
                    if (!Nodes[i].Consumed) return false;
                }
                return true;
            }
        }

        public int PendingCount
        {
            get
            {
                if (Nodes == null) return 0;
                var n = 0;
                for (var i = 0; i < Nodes.Length; i++)
                {
                    if (!Nodes[i].Consumed) n++;
                }
                return n;
            }
        }
    }
}
