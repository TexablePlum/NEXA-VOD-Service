using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Nexa.Client.Services.Notifications
{
    public interface INotificationService
    {
        ObservableCollection<AppNotification> Notifications { get; }
        void Show(string title, string message, InfoBarSeverity severity, int durationSeconds = 5);
        void ShowError(string message, string title = "Błąd");
        void ShowWarning(string message, string title = "Ostrzeżenie");
        void ShowInfo(string message, string title = "Informacja");
        void ShowSuccess(string message, string title = "Sukces");
    }

    public class NotificationService : ObservableObject, INotificationService
    {
        private readonly DispatcherQueue _dispatcherQueue;

        public ObservableCollection<AppNotification> Notifications { get; } = new();

        public NotificationService()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }

        public void Show(string title, string message, InfoBarSeverity severity, int durationSeconds = 5)
        {
            var notification = new AppNotification
            {
                Title = title,
                Message = message,
                Severity = severity,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                IsOpen = true
            };

            _dispatcherQueue.TryEnqueue(() =>
            {
                Notifications.Add(notification);

                // Uruchamia proces auto-zamykania
                if (durationSeconds > 0)
                {
                    HandleAutoClose(notification, durationSeconds);
                }
            });
        }

        private async void HandleAutoClose(AppNotification notification, int durationSeconds)
        {
            // 1. Czeka tyle, ile ma wisieć notyfikacja
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds));

            // Jeśli użytkownik zamknął ręcznie wcześniej, to IsOpen już jest false
            // Ale jeśli nie, to teraz zamyka programowo (uruchamia animację UI)
            if (notification.IsOpen)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    notification.IsOpen = false;
                });
            }

            // 2. Czeka na zakończenie animacji InfoBar (WinUI domyślnie ma ok. 300-500ms)
            // Daje bezpieczny margines 600ms, żeby nie urwać animacji w połowie
            await Task.Delay(600);

            // 3. Sprząta z pamięci
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (Notifications.Contains(notification))
                {
                    Notifications.Remove(notification);
                }
            });
        }

        public void ShowError(string message, string title = "Błąd") =>
            Show(title, message, InfoBarSeverity.Error);

        public void ShowWarning(string message, string title = "Ostrzeżenie") =>
            Show(title, message, InfoBarSeverity.Warning);

        public void ShowInfo(string message, string title = "Informacja") =>
            Show(title, message, InfoBarSeverity.Informational);

        public void ShowSuccess(string message, string title = "Sukces") =>
            Show(title, message, InfoBarSeverity.Success);
    }
}