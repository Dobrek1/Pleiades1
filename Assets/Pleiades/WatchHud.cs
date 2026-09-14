using UnityEngine;
using Pleiades.CoreSim;

namespace Pleiades
{
    public sealed class WatchHud : MonoBehaviour
    {
        SimWorld _world;
        float _payloadSlider;
        GUIStyle _title;
        GUIStyle _body;
        GUIStyle _warn;
        bool _stylesReady;

        public void Bind(SimWorld world)
        {
            _world = world;
            _payloadSlider = 0f;
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
            _stylesReady = true;
        }

        void OnGUI()
        {
            if (_world == null) return;
            EnsureStyles();

            const float pad = 12f;
            var w = 380f;
            GUI.Box(new Rect(pad, pad, w, 460f), GUIContent.none);

            float y = pad + 8f;
            var x = pad + 10f;
            const float line = 20f;

            GUI.Label(new Rect(x, y, w - 20f, 24f), "ПЛЕЯДЫ — полёт", _title);
            y += line + 6f;

            var ship = _world.Ship;
            var o = ship.Orbit;
            GUI.Label(new Rect(x, y, w - 20f, line), $"Фаза: {_world.PhaseLabelRu}", _body); y += line;
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
                $"Δv {ship.AvailableDeltaV / 1000.0:0.00} км/с   топливо {ship.FuelKg / 1000.0:0.0} т", _body);
            y += line;

            if (_world.IsThrusting)
            {
                GUI.Label(new Rect(x, y, w - 20f, line),
                    $"тяга {_world.CurrentThrustMps2:0.0} м/с²  W/S проград  Q/E радиал  Shift форсаж", _body);
            }
            else
            {
                GUI.Label(new Rect(x, y, w - 20f, line),
                    "W/S проград/ретро · Q/E радиал · Shift форсаж", _body);
            }
            y += line + 6f;

            GUI.Label(new Rect(x, y, w - 20f, line), $"Груз (пока побоку): {_payloadSlider:0} кг", _body);
            y += line;
            var newPayload = GUI.HorizontalSlider(new Rect(x, y, w - 40f, 18f), _payloadSlider, 0f, 20000f);
            if (Mathf.Abs(newPayload - _payloadSlider) > 0.5f)
            {
                _payloadSlider = newPayload;
                ship.SetPayloadKg(_payloadSlider);
            }
            y += line + 8f;

            var geo = _world.PreviewTransfer(DestinationId.Geo);
            var lun = _world.PreviewTransfer(DestinationId.Lunar);
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Гоман с текущей: ГСО {geo.TotalDeltaVKmS:0.02} км/с  Луна {lun.TotalDeltaVKmS:0.02}", _body);
            y += line + 4f;

            if (GUI.Button(new Rect(x, y, 150f, 28f), "Гоман → ГСО (G)"))
                _world.TryDepart(DestinationId.Geo);
            if (GUI.Button(new Rect(x + 160f, y, 150f, 28f), "Гоман → Луна (L)"))
                _world.TryDepart(DestinationId.Lunar);
            y += 34f;
            if (GUI.Button(new Rect(x, y, 150f, 28f), "Циркуляризовать (C)"))
            {
                var vCirc = AstroMath.CircularSpeed(o.Mu, o.RadiusM);
                var need = vCirc - o.SpeedMps;
                if (ship.TryBurn(System.Math.Abs(need), out _))
                    o.ApplyDeltaV(need, 0.0);
            }
            y += 34f;

            GUI.Label(new Rect(x, y, w - 20f, 70f),
                "Пробел пауза · 1–4 варп (старт ×3600, иначе «стоит»)\nF камера за кораблём · колёсико зум · ПКМ панорама\nТяга на варпе >60 режется до ×60",
                _body);

            if (_world.HasInterrupt)
            {
                GUI.Box(new Rect(pad, pad + 470f, w, 70f), GUIContent.none);
                GUI.Label(new Rect(x, pad + 478f, w - 20f, 54f), "⚠ " + _world.LastInterruptRu, _warn);
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
