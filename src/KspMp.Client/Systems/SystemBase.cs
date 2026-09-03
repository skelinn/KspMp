using KspMp.Net;

namespace KspMp.Systems
{
    /// <summary>
    /// A unit of multiplayer behaviour that is switched on and off by scene and connection state.
    /// Register message handlers in <see cref="OnActivate"/> and remove them in <see cref="OnDeactivate"/>.
    /// </summary>
    public abstract class SystemBase
    {
        protected SystemBase(KspMpAddon addon)
        {
            Addon = addon;
        }

        protected KspMpAddon Addon { get; }
        protected ClientNetwork Net => Addon.Network;

        public abstract string Name { get; }
        public bool Active { get; private set; }

        /// <summary>Default: run in every scene while connected.</summary>
        public virtual bool ShouldRun(GameScenes scene, bool connected) => connected;

        internal void SetActive(bool active)
        {
            if (active == Active) return;
            Active = active;
            if (active) OnActivate();
            else OnDeactivate();
        }

        protected virtual void OnActivate() { }
        protected virtual void OnDeactivate() { }
        public virtual void Update() { }
        public virtual void FixedUpdate() { }
        public virtual void LateUpdate() { }
    }
}
