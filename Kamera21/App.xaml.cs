using Kamera21.Services;
using Kamera21.Services.Interfaces;
using Kamera21.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace Kamera21
{
    public partial class App : Application
    {
        private readonly IHost _host;
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kamera21.log");
        private static readonly object _logLock = new object();

        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log($"КРИТИЧЕСКАЯ ОШИБКА: {e.ExceptionObject}");
            };

            DispatcherUnhandledException += (s, e) =>
            {
                Log($"Ошибка UI: {e.Exception.Message}");
                e.Handled = true;
            };

            Log("=== Kamera21 Запущена ===");

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Регистрируем сервисы
                    services.AddSingleton<ImageProcessor>();
                    services.AddSingleton<ComponentDetector>();
                    services.AddSingleton<InspectionService>();

                    // Регистрируем CameraService как Transient для каждого экземпляра камеры
                    services.AddTransient<ICameraService, CameraService>();

                    // Регистрируем ViewModel для камер
                    services.AddTransient<CameraViewModel>((provider) =>
                    {
                        var cameraService = provider.GetRequiredService<ICameraService>();
                        return new CameraViewModel(cameraService, "Камера");
                    });

                    // Регистрируем MainViewModel с двумя разными экземплярами камер
                    services.AddSingleton<MainViewModel>((provider) =>
                    {
                        var inspectionService = provider.GetRequiredService<InspectionService>();
                        var imageProcessor = provider.GetRequiredService<ImageProcessor>();

                        // Создаем две независимые камеры
                        var cameraService1 = provider.GetRequiredService<ICameraService>();
                        var cameraService2 = provider.GetRequiredService<ICameraService>();

                        return new MainViewModel(inspectionService, imageProcessor, cameraService1, cameraService2);
                    });

                    // Регистрируем главное окно
                    services.AddSingleton<MainWindow>();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Log("OnStartup начат");

            try
            {
                await _host.StartAsync();
                Log("Host запущен");

                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
                mainWindow.Show();

                Log("Главное окно показано");
            }
            catch (Exception ex)
            {
                Log($"Ошибка запуска приложения: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Ошибка запуска приложения: {ex.Message}",
                    "Kamera21", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            Log("=== Kamera21 Завершена ===");

            try
            {
                using (_host)
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при остановке host: {ex.Message}");
            }

            base.OnExit(e);
        }

        public static void Log(string message)
        {
            try
            {
                string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Thread:{Thread.CurrentThread.ManagedThreadId}] - {message}";

                lock (_logLock)
                {
                    File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
                }

                System.Diagnostics.Debug.WriteLine(logMessage);
            }
            catch (Exception)
            {
                // Игнорируем ошибки записи лога
            }
        }
    }
}