using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Nexa.Client.Services.Notifications;
using System;

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
            else
            {
                throw new InvalidOperationException("App.Current is not of type App.");
            }
        }
    }
}