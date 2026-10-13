using System.Windows;

namespace Arrakis.Modules
{
    public interface IModule
    {
        string Id { get; }
        string Name { get; }
        string Description { get; }
        string IconData { get; }
        string ShortcutText { get; }
        bool IsEnabled { get; set; }
        bool IsRunning { get; }
        void Start();
        void Stop();
        UIElement BuildPage();
    }

    public abstract class ModuleBase : IModule
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract string IconData { get; }
        public abstract string ShortcutText { get; }
        public abstract bool IsEnabled { get; set; }
        public bool IsRunning { get; private set; }

        public void Start()
        {
            if (IsRunning) return;
            OnStart();
            IsRunning = true;
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            OnStop();
        }

        /// <summary>Stop and start again so new settings take effect.</summary>
        public void Restart()
        {
            if (!IsRunning) return;
            Stop();
            Start();
        }

        protected abstract void OnStart();
        protected abstract void OnStop();
        public abstract UIElement BuildPage();
    }
}
