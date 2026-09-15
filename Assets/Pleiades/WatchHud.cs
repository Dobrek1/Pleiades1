using UnityEngine;
using Pleiades.CoreSim;

namespace Pleiades
{
    public sealed class WatchHud : MonoBehaviour
    {
        SimWorld _world;
        float _payloadSlider;
        float _debugFuelSlider = -1f;
        Vector2 _slotScroll;
        GUIStyle _title;
        GUIStyle _body;
        GUIStyle _warn;
        GUIStyle _debug;
        bool _stylesReady;

        /// <summary>Shown in HUD so Play can confirm the exe matches git.</summary>
        public const string BuildId = "a01b3e2";

        public void Bind(SimWorld world)
        {
            _world = world;
            _payloadSlider = 0f;
            _debugFuelSlider = -1f;
            _slotScroll = Vector2.zero;
        }

        void EnsureStyles()
        {
            if (_stylesReady) return;
            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.95f, 0.9f, 0.6f) }
            };
            _body = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.85f, 0.9f, 1f) }
            };
            _warn = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(1f, 0.45f, 0.35f) }
            };
            _debug = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(1f, 0.7f, 0.3f) }
            };
            _stylesReady = true;
        }

        void OnGUI()
        {
            if (_world == null) return;
            EnsureStyles();

            const float pad = 12f;
            var w = 420f;
            const float boxH = 760f;
            GUI.Box(new Rect(pad, pad, w, boxH), GUIContent.none);

            float y = pad + 8f;
            var x = pad + 10f;
            const float line = 20f;

            GUI.Label(new Rect(x, y, w - 20f, 24f), "ПЛЕЯДЫ · " + BuildId + " · слоты/узлы", _title);
            y += line + 6f;

            var ship = _world.Ship;
            var o = ship.Orbit;
            GUI.Label(new Rect(x, y, w - 20f, line), $"Фаза: {_world.PhaseLabelRu}  ·  цель: {_world.DestinationLabelRu}", _body); y += line;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Время: {FormatTime(_world.SimTimeSeconds)}  |  {(_world.Paused ? "ПАУЗА" : "ХОД")}  варп ×{_world.WarpFactor:0}", _body);
            y += line;

            var altKm = (o.RadiusM - GravityBody.Earth.RadiusM) / 1000.0;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"r {o.RadiusM / 1000.0:0} км   h≈{altKm:0} км   v {o.SpeedMps:0} м/с", _body);
            y += line;

            var apo = o.ApoapsisM;
            var apoStr = double.IsInfinity(apo) ? "∞" : (apo / 1000.0).ToString("0");
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"пери {o.PeriapsisM / 1000.0:0} км   апо {apoStr} км   e {o.Eccentricity:0.000}", _body);
            y += line;

            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Δv {ship.AvailableDeltaV / 1000.0:0.0} км/с (рак +debug {ship.DebugExtraDeltaVLeft / 1000.0:0.0})  LH2 {ship.FuelKg / 1000.0:0.0}/{ship.FuelCapacityKg / 1000.0:0} т", _body);
            y += line;

            GUI.Label(new Rect(x, y, w - 20f, line),
                _world.IsThrusting
                    ? $"тяга {_world.CurrentThrustMps2:0.0} м/с²  W/S Q/E  Shift"
                    : "W/S проград/ретро · Q/E радиал · Shift форсаж", _body);
            y += line + 8f;

            // --- Orbit slots ---
            GUI.Label(new Rect(x, y, w - 20f, line), "Слоты орбит (клик = превью)", _title);
            y += line + 2f;

            var slots = OrbitSlotCatalog.ForBody(OrbitBodyId.Earth);
            const float rowH = 26f;
            const float visibleRows = 8f;
            var scrollViewH = visibleRows * rowH;
            var contentH = slots.Count * rowH + 4f;
            var scrollRect = new Rect(x, y, w - 24f, scrollViewH);
            var viewRect = new Rect(0f, 0f, w - 48f, contentH);
            _slotScroll = GUI.BeginScrollView(scrollRect, _slotScroll, viewRect);
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var selected = _world.SelectedSlot != null && _world.SelectedSlot.Id == slot.Id;
                var label = selected ? "► " + slot.DisplayNameRu : slot.DisplayNameRu;
                if (GUI.Button(new Rect(0f, i * rowH, w - 52f, rowH - 2f), label))
                    _world.SelectSlot(slot);
            }
            GUI.EndScrollView();
            y += scrollViewH + 6f;

            var plan = _world.PreviewSelectedSlot();
            if (_world.SelectedSlot == null)
            {
                GUI.Label(new Rect(x, y, w - 20f, line), "Слот не выбран", _body);
                y += line;
            }
            else if (!plan.Ok)
            {
                GUI.Label(new Rect(x, y, w - 20f, 40f), plan.FailRu, _warn);
                y += 42f;
            }
            else
            {
                var t = plan.Transfer;
                GUI.Label(new Rect(x, y, w - 20f, line),
                    $"Превью → {_world.SelectedSlot.DisplayNameRu}", _body);
                y += line;
                GUI.Label(new Rect(x, y, w - 20f, line),
                    $"Δv1 {t.DepartureDeltaV / 1000.0:0.00} · Δv2 {t.ArrivalDeltaV / 1000.0:0.00} · итого {t.TotalDeltaVKmS:0.00} км/с", _body);
                y += line;
                GUI.Label(new Rect(x, y, w - 20f, line),
                    $"TOF {FormatTime(t.TimeOfFlightSeconds)}", _body);
                y += line;
            }

            y += 4f;
            if (GUI.Button(new Rect(x, y, 200f, 28f), "Исполнить уход"))
                _world.TryDepartSelectedSlot();
            if (_world.HasLivePlan)
            {
                if (GUI.Button(new Rect(x + 210f, y, 140f, 28f), "Отменить план"))
                    _world.CancelPlan();
            }
            y += 34f;

            if (_world.HasLivePlan)
            {
                GUI.Label(new Rect(x, y, w - 20f, line),
                    "План жив → варп ≤×60 (после отмены/прибытия снова 1/60/3600/86400)", _debug);
                y += line + 2f;
            }

            // Armed Layer B plan nodes
            var armed = _world.ActivePlan;
            if (armed != null && armed.Nodes != null)
            {
                GUI.Label(new Rect(x, y, w - 20f, line),
                    $"Активный план B → {armed.Slot.DisplayNameRu} (импульсы, не непрерывный огонь)", _body);
                y += line;
                for (var i = 0; i < armed.Nodes.Length; i++)
                {
                    var n = armed.Nodes[i];
                    var tag = n.Consumed ? "✓" : "○";
                    var kind = i == 0 ? "уход" : "прибытие";
                    var dv = System.Math.Sqrt(n.DvPrograde * n.DvPrograde + n.DvRadial * n.DvRadial);
                    GUI.Label(new Rect(x, y, w - 20f, line),
                        $"{tag} {kind}: t={FormatTime(n.T)}  Δv {dv / 1000.0:0.00} км/с  ν={n.TrueAnomalyRad:0.00}",
                        _body);
                    y += line;
                }
                GUI.Label(new Rect(x, y, w - 20f, line),
                    $"прибытие sim {FormatTime(armed.ArrivalSimTime)}  ·  фаза Луны@arr {armed.TargetMoonAnomalyAtArrival:0.00}",
                    _body);
                y += line + 4f;
            }

            if (GUI.Button(new Rect(x, y, 220f, 28f), "Цирк. вокруг Земли (C)"))
                _world.TryCircularizeHere();
            y += 34f;

            GUI.Label(new Rect(x, y, w - 20f, 48f),
                "Клик = превью; «Исполнить уход» ставит 2 узла (уход+прибытие).\nL-точки = маркеры. Импульсы по t и аномалии (B).",
                _body);
            y += 52f;

            // payload
            GUI.Label(new Rect(x, y, w - 20f, line), $"Груз: {_payloadSlider:0} кг", _body);
            y += line;
            var newPayload = GUI.HorizontalSlider(new Rect(x, y, w - 40f, 18f), _payloadSlider, 0f, 20000f);
            if (Mathf.Abs(newPayload - _payloadSlider) > 0.5f)
            {
                _payloadSlider = newPayload;
                ship.SetPayloadKg(_payloadSlider);
            }
            y += line + 8f;

            // debug fuel
            if (_debugFuelSlider < 0f)
                _debugFuelSlider = (float)ship.FuelKg;
            GUI.Label(new Rect(x, y, w - 20f, line), $"debug LH2 (пополнение Δv): {_debugFuelSlider / 1000f:0.0} т", _debug);
            y += line;
            var newFuel = GUI.HorizontalSlider(new Rect(x, y, w - 40f, 18f), _debugFuelSlider, 0f, (float)ship.FuelCapacityKg);
            if (Mathf.Abs(newFuel - _debugFuelSlider) > 1f)
            {
                _debugFuelSlider = newFuel;
                ship.SetFuelKg(_debugFuelSlider);
            }
            y += line + 6f;

            GUI.Label(new Rect(x, y, w - 20f, 50f),
                "Пробел пауза · 1–4 варп · F камера · L/G/H цели · C цирк. Земли\nРучная тяга остаётся. Тяга на варпе >60 → ×60.",
                _body);

            if (_world.HasInterrupt)
            {
                GUI.Box(new Rect(pad, pad + boxH + 8f, w, 70f), GUIContent.none);
                GUI.Label(new Rect(x, pad + boxH + 16f, w - 20f, 54f), "⚠ " + _world.LastInterruptRu, _warn);
            }
        }

        static string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var d = (int)(seconds / 86400.0);
            seconds -= d * 86400.0;
            var h = (int)(seconds / 3600.0);
            seconds -= h * 3600.0;
            var m = (int)(seconds / 60.0);
            var s = (int)(seconds - m * 60.0);
            if (d > 0) return $"{d}д {h:00}:{m:00}:{s:00}";
            return $"{h:00}:{m:00}:{s:00}";
        }
    }
}
