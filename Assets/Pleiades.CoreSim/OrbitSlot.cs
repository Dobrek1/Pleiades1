using System;
using System.Collections.Generic;

namespace Pleiades.CoreSim
{
    public enum OrbitBodyId
    {
        Earth = 0,
        Moon = 1,
    }

    public enum OrbitSlotKind
    {
        CircularAltitude = 0,
        MarkerOnly = 1,
    }

    public sealed class OrbitSlot
    {
        public string Id { get; }
        public OrbitBodyId Body { get; }
        public string DisplayNameRu { get; }
        public OrbitSlotKind Kind { get; }
        /// <summary>Circular radius from body center; 0 for MarkerOnly.</summary>
        public double RadiusM { get; }
        /// <summary>L1/L2/… hint for markers; empty for circular slots.</summary>
        public string PositionHint { get; }

        public OrbitSlot(
            string id,
            OrbitBodyId body,
            string displayNameRu,
            OrbitSlotKind kind,
            double radiusM,
            string positionHint = "")
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Body = body;
            DisplayNameRu = displayNameRu ?? "";
            Kind = kind;
            RadiusM = radiusM;
            PositionHint = positionHint ?? "";
        }
    }

    public static class OrbitSlotCatalog
    {
        static readonly OrbitSlot[] Slots;
        static readonly Dictionary<string, OrbitSlot> ById;

        static OrbitSlotCatalog()
        {
            var re = GravityBody.Earth.RadiusM;
            Slots = new[]
            {
                new OrbitSlot("earth-leo-200", OrbitBodyId.Earth, "LEO 200 км",
                    OrbitSlotKind.CircularAltitude, re + 200_000.0),
                new OrbitSlot("earth-parking-400", OrbitBodyId.Earth, "Parking 400 км",
                    OrbitSlotKind.CircularAltitude, re + 400_000.0),
                new OrbitSlot("earth-meo-20k", OrbitBodyId.Earth, "MEO 20 000 км",
                    OrbitSlotKind.CircularAltitude, re + 20_000_000.0),
                new OrbitSlot("earth-gso", OrbitBodyId.Earth, "ГСО",
                    OrbitSlotKind.CircularAltitude, GravityBody.GeoStationaryRadiusM),
                new OrbitSlot("earth-high-rmoon", OrbitBodyId.Earth, "Высокая · r Луны (вокруг Земли)",
                    OrbitSlotKind.CircularAltitude, GravityBody.MoonOrbitRadiusM),
                new OrbitSlot("earth-l1", OrbitBodyId.Earth, "L1",
                    OrbitSlotKind.MarkerOnly, 0.0, "L1"),
                new OrbitSlot("earth-l2", OrbitBodyId.Earth, "L2",
                    OrbitSlotKind.MarkerOnly, 0.0, "L2"),
                new OrbitSlot("earth-l4", OrbitBodyId.Earth, "L4",
                    OrbitSlotKind.MarkerOnly, 0.0, "L4"),
                new OrbitSlot("earth-l5", OrbitBodyId.Earth, "L5",
                    OrbitSlotKind.MarkerOnly, 0.0, "L5"),
            };

            ById = new Dictionary<string, OrbitSlot>(Slots.Length);
            for (var i = 0; i < Slots.Length; i++)
                ById[Slots[i].Id] = Slots[i];
        }

        public static IReadOnlyList<OrbitSlot> All => Slots;

        public static OrbitSlot Get(string id)
        {
            if (id == null || !ById.TryGetValue(id, out var slot))
                throw new KeyNotFoundException("OrbitSlot id not found: " + id);
            return slot;
        }

        public static IReadOnlyList<OrbitSlot> ForBody(OrbitBodyId body)
        {
            var list = new List<OrbitSlot>();
            for (var i = 0; i < Slots.Length; i++)
            {
                if (Slots[i].Body == body)
                    list.Add(Slots[i]);
            }
            return list;
        }
    }
}
