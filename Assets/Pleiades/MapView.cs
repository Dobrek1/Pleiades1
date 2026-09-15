using UnityEngine;
using Pleiades.CoreSim;

namespace Pleiades
{
    public sealed class MapView : MonoBehaviour
    {
        SimWorld _world;
        Transform _earth;
        Transform _moon;
        Transform _ship;
        Transform _moonPlanMarker;
        LineRenderer _leoRing;
        LineRenderer _geoRing;
        LineRenderer _moonOrbitRing;
        LineRenderer _shipOrbit;
        LineRenderer _transferArc;

        static readonly Color EarthColor = new Color(0.18f, 0.42f, 0.78f);
        static readonly Color MoonColor = new Color(0.75f, 0.75f, 0.7f);
        static readonly Color ShipColor = new Color(1f, 0.88f, 0.35f);
        static readonly Color OrbitColor = new Color(0.4f, 0.7f, 1f, 0.35f);
        static readonly Color GeoColor = new Color(0.3f, 1f, 0.6f, 0.35f);
        static readonly Color MoonOrbitColor = new Color(0.7f, 0.7f, 0.7f, 0.28f);
        static readonly Color ShipOrbitColor = new Color(1f, 0.75f, 0.2f, 0.9f);
        static readonly Color TransferColor = new Color(1f, 0.45f, 0.15f, 0.7f);
        static readonly Color ThrustColor = new Color(1f, 0.35f, 0.1f);
        static readonly Color MoonPlanMarkerColor = new Color(1f, 0.55f, 0.85f, 0.95f);

        public void Bind(SimWorld world) => _world = world;

        void Start() => BuildPrimitives();

        void BuildPrimitives()
        {
            _earth = CreateSphere("Earth", EarthColor, (float)AstroMath.MetersToUnits(GravityBody.Earth.RadiusM));
            _moon = CreateSphere("Moon", MoonColor, Mathf.Max(1.4f, (float)AstroMath.MetersToUnits(GravityBody.Moon.RadiusM)));
            _ship = CreateSphere("Ship", ShipColor, 1.1f);
            // Arrival-phase marker (not a second Moon): flat diamond + label color.
            var markerGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            markerGo.name = "MoonPlanTargetArr";
            markerGo.transform.SetParent(transform, false);
            markerGo.transform.localScale = new Vector3(2.2f, 0.35f, 2.2f);
            markerGo.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            var mcol = markerGo.GetComponent<Collider>();
            if (mcol != null) Object.Destroy(mcol);
            var mr = markerGo.GetComponent<Renderer>();
            if (mr != null) mr.material.color = MoonPlanMarkerColor;
            _moonPlanMarker = markerGo.transform;
            _moonPlanMarker.gameObject.SetActive(false);

            var rLeo = GravityBody.Earth.RadiusM + 200_000.0;
            _leoRing = CreateRing("LeoOrbit", OrbitColor, (float)AstroMath.MetersToUnits(rLeo), 128);
            _geoRing = CreateRing("GeoOrbit", GeoColor, (float)AstroMath.MetersToUnits(GravityBody.GeoStationaryRadiusM), 128);
            _moonOrbitRing = CreateRing("MoonOrbit", MoonOrbitColor, (float)AstroMath.MetersToUnits(GravityBody.MoonOrbitRadiusM), 192);
            _shipOrbit = CreateLine("ShipOrbit", ShipOrbitColor, 129);
            _shipOrbit.loop = true;
            _transferArc = CreateLine("TransferArc", TransferColor, 64);
        }

        Transform CreateSphere(string name, Color color, float radiusUu)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * Mathf.Max(radiusUu * 2f, 0.5f);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.material.color = color;
            return go.transform;
        }

        LineRenderer CreateRing(string name, Color color, float radiusUu, int segments)
        {
            var lr = CreateLine(name, color, segments + 1);
            lr.loop = true;
            for (var i = 0; i <= segments; i++)
            {
                var a = (float)(i * 2.0 * System.Math.PI / segments);
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radiusUu, 0f, Mathf.Sin(a) * radiusUu));
            }
            return lr;
        }

        LineRenderer CreateLine(string name, Color color, int count)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = count;
            lr.loop = false;
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.4f;
            lr.startColor = color;
            lr.endColor = color;
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            lr.material = new Material(sh) { color = color };
            lr.startWidth = 0.4f;
            lr.endWidth = 0.4f;
            return lr;
        }

        public void Refresh()
        {
            if (_world == null || _earth == null) return;

            _earth.position = Vector3.zero;

            _world.GetMoonPositionMeters(out var mx, out var my, out var mz);
            _moon.position = new Vector3(
                (float)AstroMath.MetersToUnits(mx),
                (float)AstroMath.MetersToUnits(my),
                (float)AstroMath.MetersToUnits(mz));

            _world.Ship.Orbit.GetPositionMeters(out var sx, out var sy, out var sz);
            _ship.position = new Vector3(
                (float)AstroMath.MetersToUnits(sx),
                (float)AstroMath.MetersToUnits(sy),
                (float)AstroMath.MetersToUnits(sz));

            var rend = _ship.GetComponent<Renderer>();
            if (rend != null)
                rend.material.color = _world.IsThrusting ? ThrustColor : ShipColor;

            if (_moonPlanMarker != null)
            {
                _world.GetPlanMoonTargetMeters(out var px, out var py, out var pz, out var ok);
                _moonPlanMarker.gameObject.SetActive(ok && _world.ActivePlan != null);
                if (ok)
                {
                    _moonPlanMarker.position = new Vector3(
                        (float)AstroMath.MetersToUnits(px),
                        (float)AstroMath.MetersToUnits(py),
                        (float)AstroMath.MetersToUnits(pz));
                }
            }

            DrawShipOrbit();
            UpdateTransferArc();
        }

        void DrawShipOrbit()
        {
            if (_shipOrbit == null) return;
            var o = _world.Ship.Orbit;
            if (o.Eccentricity >= 0.98 || double.IsInfinity(o.SemiMajorM))
            {
                _shipOrbit.enabled = false;
                return;
            }
            _shipOrbit.enabled = true;
            var a = o.SemiMajorM;
            var e = o.Eccentricity;
            var w = System.Math.Atan2(o.Rz, o.Rx);
            EccArg(o, out w);
            var n = _shipOrbit.positionCount;
            for (var i = 0; i < n; i++)
            {
                var nu = i / (double)(n - 1) * System.Math.PI * 2.0;
                var rr = a * (1.0 - e * e) / (1.0 + e * System.Math.Cos(nu));
                if (rr < 0) rr = a;
                var x = rr * System.Math.Cos(nu);
                var z = rr * System.Math.Sin(nu);
                var cw = System.Math.Cos(w);
                var sw = System.Math.Sin(w);
                _shipOrbit.SetPosition(i, new Vector3(
                    (float)AstroMath.MetersToUnits(cw * x - sw * z),
                    0.05f,
                    (float)AstroMath.MetersToUnits(sw * x + cw * z)));
            }
        }

        static void EccArg(KeplerOrbit o, out double w)
        {
            var r = o.RadiusM;
            var mu = o.Mu;
            var v2 = o.Vx * o.Vx + o.Vz * o.Vz;
            var rv = o.Rx * o.Vx + o.Rz * o.Vz;
            var ex = ((v2 - mu / r) * o.Rx - rv * o.Vx) / mu;
            var ez = ((v2 - mu / r) * o.Rz - rv * o.Vz) / mu;
            w = System.Math.Atan2(ez, ex);
        }

        void UpdateTransferArc()
        {
            if (_transferArc == null) return;
            var t = _world.ActiveTransfer;
            var show = t.TimeOfFlightSeconds > 0.0 && t.R1 > 0.0 && t.R2 > 0.0
                && (_world.ActivePlan != null
                    || _world.Destination != DestinationId.None
                    || _world.DestinationLabelRu != "свободный полёт");
            _transferArc.enabled = show;
            if (!show) return;

            var n = _transferArc.positionCount;
            for (var i = 0; i < n; i++)
            {
                var u = i / (double)(n - 1);
                var r = SampleTransferR(t, u);
                var nu = System.Math.PI * u;
                if (t.R2 < t.R1) nu = System.Math.PI * (1.0 - u);
                var x = (float)AstroMath.MetersToUnits(r * System.Math.Cos(nu));
                var z = (float)AstroMath.MetersToUnits(r * System.Math.Sin(nu));
                _transferArc.SetPosition(i, new Vector3(x, 0.1f, z));
            }
        }

        static double SampleTransferR(Hohmann.Transfer t, double u)
        {
            var e = System.Math.Abs(t.R2 - t.R1) / (t.R1 + t.R2);
            var a = t.SemiMajor;
            var nu = System.Math.PI * u;
            if (t.R2 < t.R1) nu = System.Math.PI * (1.0 - u);
            var denom = 1.0 + e * System.Math.Cos(nu);
            if (System.Math.Abs(denom) < 1e-9) return t.R2;
            return a * (1.0 - e * e) / denom;
        }
    }
}
