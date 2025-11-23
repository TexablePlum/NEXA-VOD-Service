using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Nexa.Client.Services.Notifications
{
    public partial class AppNotification : ObservableObject
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(5);
        public Guid Id { get; } = Guid.NewGuid();

        [ObservableProperty]
        private bool _isOpen = true;
    }
}