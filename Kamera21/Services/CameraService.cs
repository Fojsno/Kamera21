using OpenCvSharp;
using Kamera21.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kamera21.Services
{
    public class CameraService : ICameraService, IDisposable
    {
        private VideoCapture? _capture;
        private Thread? _captureThread;
        private bool _isLiveStreaming = false;
        private bool _disposed = false;
        private int _connectionAttempts = 0;
        private const int MAX_CONNECTION_ATTEMPTS = 3;
        private readonly object _captureLock = new object();
        private CancellationTokenSource? _cancellationTokenSource;
        private DateTime _lastFrameTime = DateTime.MinValue;
        private double _targetFrameIntervalMs = 33; // ~30 FPS по умолчанию

        public bool IsConnected => _capture?.IsOpened() ?? false;
        public int CameraIndex { get; set; } = 0;
        public string CameraUrl { get; set; } = string.Empty;
        public Size FrameSize { get; set; } = new Size(1280, 720);
        public double FPS { get; private set; } = 30;
        public string LastError { get; private set; } = string.Empty;

        public event FrameCapturedEventHandler? FrameCaptured;

        public async Task<bool> ConnectAsync()
        {
            LastError = string.Empty;
            _connectionAttempts = 0;

            return await Task.Run(() =>
            {
                try
                {
                    // Закрываем предыдущее соединение
                    DisconnectAsync().Wait();

                    for (int attempt = 1; attempt <= MAX_CONNECTION_ATTEMPTS; attempt++)
                    {
                        _connectionAttempts = attempt;
                        App.Log($"Попытка подключения #{attempt} к камере");

                        try
                        {
                            // Определяем тип подключения
                            if (!string.IsNullOrEmpty(CameraUrl))
                            {
                                // Пробуем распарсить как число (индекс камеры)
                                if (int.TryParse(CameraUrl.Trim(), out int cameraIndex))
                                {
                                    App.Log($"Подключаемся к локальной камере с индексом: {cameraIndex}");
                                    _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.ANY);
                                }
                                else
                                {
                                    // Используем как URL
                                    App.Log($"Подключаемся к IP-камере: {CameraUrl}");
                                    _capture = new VideoCapture(CameraUrl, VideoCaptureAPIs.ANY);
                                }
                            }
                            else
                            {
                                App.Log($"Подключаемся к локальной камере с индексом по умолчанию: {CameraIndex}");
                                _capture = new VideoCapture(CameraIndex, VideoCaptureAPIs.ANY);
                            }

                            // Даем камере время на инициализацию
                            Thread.Sleep(500);

                            if (_capture == null)
                            {
                                LastError = "Ошибка создания объекта камеры";
                                App.Log(LastError);
                                continue;
                            }

                            if (!_capture.IsOpened())
                            {
                                LastError = "Камера не открылась";
                                App.Log(LastError);
                                continue;
                            }

                            // Устанавливаем параметры камеры для лучшей производительности
                            _capture.Set(VideoCaptureProperties.BufferSize, 1); // Минимизируем буфер

                            // Пробуем получить кадр для проверки
                            bool frameRead = false;
                            using (var testFrame = new Mat())
                            {
                                for (int i = 0; i < 10; i++)
                                {
                                    if (_capture.Read(testFrame) && !testFrame.Empty())
                                    {
                                        FrameSize = new Size(testFrame.Width, testFrame.Height);
                                        frameRead = true;
                                        App.Log($"Кадр получен: {testFrame.Width}x{testFrame.Height}");
                                        break;
                                    }
                                    Thread.Sleep(100);
                                }
                            }

                            if (!frameRead)
                            {
                                LastError = "Не удалось получить кадр с камеры";
                                App.Log(LastError);
                                _capture.Release();
                                _capture.Dispose();
                                _capture = null;
                                continue;
                            }

                            // Получаем FPS
                            FPS = _capture.Get(VideoCaptureProperties.Fps);
                            if (FPS <= 0 || FPS > 60) FPS = 30;

                            _targetFrameIntervalMs = 1000.0 / FPS;

                            App.Log($"Камера подключена успешно! FPS: {FPS}, Размер: {FrameSize}");
                            LastError = string.Empty;
                            return true;
                        }
                        catch (Exception ex)
                        {
                            LastError = $"Попытка #{attempt} не удалась: {ex.Message}";
                            App.Log(LastError);
                            _capture?.Dispose();
                            _capture = null;

                            if (attempt < MAX_CONNECTION_ATTEMPTS)
                            {
                                Thread.Sleep(1000); // Ждем перед следующей попыткой
                            }
                        }
                    }

                    App.Log($"Все {MAX_CONNECTION_ATTEMPTS} попытки подключения не удались");
                    return false;
                }
                catch (Exception ex)
                {
                    LastError = $"Критическая ошибка подключения: {ex.Message}";
                    App.Log(LastError);
                    return false;
                }
            });
        }

        public Task DisconnectAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    StopLiveStream();
                    lock (_captureLock)
                    {
                        _capture?.Release();
                        _capture?.Dispose();
                        _capture = null;
                        _isLiveStreaming = false;
                    }
                    App.Log("Камера отключена");
                }
                catch (Exception ex)
                {
                    App.Log($"Ошибка при отключении камеры: {ex.Message}");
                }
            });
        }

        public Task<Mat> CaptureFrameAsync()
        {
            return Task.Run(() =>
            {
                lock (_captureLock)
                {
                    if (_capture == null || !_capture.IsOpened())
                        throw new InvalidOperationException("Камера не подключена");

                    var frame = new Mat();
                    for (int i = 0; i < 10; i++) // Пробуем несколько раз
                    {
                        if (_capture.Read(frame) && !frame.Empty())
                            return frame;

                        Thread.Sleep(50);
                    }

                    throw new InvalidOperationException("Не удалось захватить кадр");
                }
            });
        }

        public Task StartLiveStream()
        {
            return Task.Run(() =>
            {
                lock (_captureLock)
                {
                    if (_capture == null || !_capture.IsOpened())
                        throw new InvalidOperationException("Камера не подключена");

                    if (_isLiveStreaming) return;

                    _isLiveStreaming = true;
                    _cancellationTokenSource = new CancellationTokenSource();

                    // Запускаем поток для захвата кадров
                    _captureThread = new Thread(LiveCaptureThread)
                    {
                        Name = "CameraLiveCaptureThread",
                        IsBackground = true,
                        Priority = ThreadPriority.BelowNormal
                    };

                    _captureThread.Start(_cancellationTokenSource.Token);

                    App.Log($"Live-трансляция запущена. FPS: {FPS}");
                }
            });
        }

        private void LiveCaptureThread(object? obj)
        {
            if (obj is not CancellationToken cancellationToken)
                return;

            App.Log($"Поток захвата кадров запущен");

            try
            {
                while (!cancellationToken.IsCancellationRequested && _isLiveStreaming)
                {
                    try
                    {
                        var frameStartTime = DateTime.Now;

                        Mat? frame = null;
                        lock (_captureLock)
                        {
                            if (_capture == null || !_capture.IsOpened())
                                break;

                            frame = new Mat();
                            if (!_capture.Read(frame) || frame.Empty())
                            {
                                frame.Dispose();
                                continue;
                            }
                        }

                        if (frame != null && !frame.Empty())
                        {
                            // Вызываем событие в основном потоке через Dispatcher
                            FrameCaptured?.Invoke(this, new FrameCapturedEventArgs(frame));
                            frame.Dispose();
                        }

                        // Регулируем FPS
                        var processingTime = (DateTime.Now - frameStartTime).TotalMilliseconds;
                        var sleepTime = Math.Max(0, _targetFrameIntervalMs - processingTime);

                        if (sleepTime > 0)
                        {
                            Thread.Sleep((int)sleepTime);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Log($"Ошибка в потоке захвата: {ex.Message}");
                        Thread.Sleep(100);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"Критическая ошибка в потоке захвата: {ex.Message}");
            }
            finally
            {
                App.Log("Поток захвата кадров остановлен");
            }
        }

        public Task StopLiveStream()
        {
            return Task.Run(() =>
            {
                lock (_captureLock)
                {
                    _isLiveStreaming = false;

                    // Останавливаем поток захвата
                    if (_cancellationTokenSource != null)
                    {
                        _cancellationTokenSource.Cancel();
                        _cancellationTokenSource.Dispose();
                        _cancellationTokenSource = null;
                    }

                    // Ждем завершения потока
                    if (_captureThread != null && _captureThread.IsAlive)
                    {
                        _captureThread.Join(1000);
                    }

                    _captureThread = null;

                    App.Log("Live-трансляция остановлена");
                }
            });
        }

        public Task StartContinuousCapture() => StartLiveStream();
        public Task StopContinuousCapture() => StopLiveStream();

        // Новый метод для получения списка камер
        public List<CameraInfo> GetAvailableCameras()
        {
            var cameras = new List<CameraInfo>();

            App.Log("Поиск доступных камер...");

            // Проверяем первые 10 индексов (обычно достаточно)
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using (var testCapture = new VideoCapture(i, VideoCaptureAPIs.ANY))
                    {
                        Thread.Sleep(100); // Даем время на инициализацию

                        if (testCapture.IsOpened())
                        {
                            string cameraName = GetCameraName(i);

                            cameras.Add(new CameraInfo
                            {
                                Index = i,
                                Name = cameraName,
                                IsAvailable = true
                            });

                            App.Log($"Найдена камера {i}: {cameraName}");
                            testCapture.Release();
                        }
                        else
                        {
                            App.Log($"Камера {i} не найдена");
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"Ошибка проверки камеры {i}: {ex.Message}");
                }
            }

            // Добавляем также опцию для IP-камеры
            cameras.Add(new CameraInfo
            {
                Index = -1,
                Name = "IP-камера (ввести URL)",
                IsAvailable = true
            });

            return cameras;
        }

        private string GetCameraName(int index)
        {
            try
            {
                // Попробуем получить имя камеры через DirectShow (если доступно)
                return $"USB Camera {index}";
            }
            catch
            {
                return $"Камера {index}";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                StopLiveStream();

                lock (_captureLock)
                {
                    _capture?.Release();
                    _capture?.Dispose();
                    _capture = null;
                }

                _cancellationTokenSource?.Dispose();
            }
            catch (Exception ex)
            {
                App.Log($"Ошибка при Dispose камеры: {ex.Message}");
            }
            finally
            {
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}