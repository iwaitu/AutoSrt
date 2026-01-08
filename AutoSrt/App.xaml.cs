using Microsoft.Extensions.DependencyInjection;

namespace AutoSrt
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell())
            {
                Title = "AutoSrt Agent",
                Width = 800,
                Height = 1000,
                MinimumWidth = 600,
                MaximumWidth = 800,
                MinimumHeight = 800,
                MaximumHeight = 1000
            };

            return window;
        }
    }
}