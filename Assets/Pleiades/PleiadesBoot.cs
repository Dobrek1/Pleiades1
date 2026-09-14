using UnityEngine;
using Pleiades.CoreSim;

namespace Pleiades
{
    /// <summary>
    /// Single entry MonoBehaviour: builds Play scene (camera, map, HUD, sim).
    /// Attach to any GameObject in an empty scene, or use [RuntimeInitializeOnLoad].
    /// </summary>
    public sealed class PleiadesBoot : MonoBehaviour
    {
        public static PleiadesBoot Instance { get; private set; }

        public SimWorld World { get; private set; }
        public MapView Map { get; private set; }
        public WatchHud Hud { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (Object.FindAnyObjectByType<PleiadesBoot>() != null)
                return;
            var go = new GameObject("PleiadesBoot");
            go.AddComponent<PleiadesBoot>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            World = new SimWorld();

            EnsureCamera();
            Map = gameObject.AddComponent<MapView>();
            Map.Bind(World);
            Hud = gameObject.AddComponent<WatchHud>();
            Hud.Bind(World);
        }

        void EnsureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                camGo.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            cam.orthographic = true;
            // Earth radius ~6.37 uu; LEO frame — start zoomed to ~80 uu half-extent
            cam.orthographicSize = 80f;
            cam.transform.position = new Vector3(0f, 200f, 0f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 10000f;
        }

        void Update()
        {
            if (World == null) return;
            HandleInput();
            World.Tick(Time.unscaledDeltaTime);
            if (Map != null) Map.Refresh();
        }

        void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                World.TogglePause();

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                World.SetWarpIndex(0);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                World.SetWarpIndex(1);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                World.SetWarpIndex(2);
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
                World.SetWarpIndex(3);

            if (Input.GetKeyDown(KeyCode.G))
                TryDepart(DestinationId.Geo);
            if (Input.GetKeyDown(KeyCode.L))
                TryDepart(DestinationId.Lunar);

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return))
                World.ClearInterrupt();

            var cam = Camera.main;
            if (cam == null) return;

            float scroll = Input.mouseScrollDelta.y;
            if (System.Math.Abs(scroll) > 0.01f)
            {
                float size = cam.orthographicSize * (scroll > 0f ? 0.85f : 1.15f);
                cam.orthographicSize = Mathf.Clamp(size, 8f, 800f);
            }

            if (Input.GetMouseButton(1))
            {
                float dx = Input.GetAxis("Mouse X");
                float dy = Input.GetAxis("Mouse Y");
                float scale = cam.orthographicSize * 0.05f;
                // Top-down +Y camera: pan in XZ
                cam.transform.position += new Vector3(-dx * scale, 0f, -dy * scale);
            }
        }

        void TryDepart(DestinationId dest)
        {
            World.ClearInterrupt();
            if (!World.TryDepart(dest))
            {
                if (!World.HasInterrupt)
                {
                    // Soft fail without interrupt already raised
                    World.SetPaused(true);
                }
            }
        }
    }
}
