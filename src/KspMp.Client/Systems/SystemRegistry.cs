using System;
using System.Collections.Generic;

namespace KspMp.Systems
{
    public sealed class SystemRegistry
    {
        private readonly List<SystemBase> _systems = new List<SystemBase>();

        public IReadOnlyList<SystemBase> Systems => _systems;

        public void Add(SystemBase system) => _systems.Add(system);

        /// <summary>Re-evaluates which systems run for the given scene and connection state.</summary>
        public void Refresh(GameScenes scene, bool connected)
        {
            foreach (var system in _systems)
            {
                try
                {
                    // Never inside somebody's private save: connected at the main menu and then resuming a
                    // career would otherwise upload that career's vessels into the shared universe.
                    var inGame = HighLogic.LoadedSceneIsGame;
                    var allowed = !inGame || KspMp.Game.SessionStarter.IsInMultiplayerSave;
                    system.SetActive(allowed && system.ShouldRun(scene, connected));
                }
                catch (Exception e)
                {
                    Log.Exception("Toggling system " + system.Name, e);
                }
            }
        }

        public void Update()
        {
            foreach (var system in _systems)
            {
                if (!system.Active) continue;
                try { system.Update(); }
                catch (Exception e) { Log.Exception(system.Name + ".Update", e); }
            }
        }

        public void FixedUpdate()
        {
            foreach (var system in _systems)
            {
                if (!system.Active) continue;
                try { system.FixedUpdate(); }
                catch (Exception e) { Log.Exception(system.Name + ".FixedUpdate", e); }
            }
        }

        public void LateUpdate()
        {
            foreach (var system in _systems)
            {
                if (!system.Active) continue;
                try { system.LateUpdate(); }
                catch (Exception e) { Log.Exception(system.Name + ".LateUpdate", e); }
            }
        }
    }
}
