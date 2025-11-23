using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Nexa.Client.Services.Notifications;

namespace Nexa.Client.Views.Controls
{
    public sealed partial class NotificationOverlay : UserControl
    {
        public INotificationService ViewModel { get; }

        public NotificationOverlay()
        {
            this.InitializeComponent();

            if (App.Current is App app)
            {
                ViewModel = app.Services.GetRequiredService<INotificationService>();
            }
        }
    }
}