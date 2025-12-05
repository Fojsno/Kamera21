using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kamera21.Services.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kamera21.ViewModels
{
    public partial class CameraViewModel : ObservableObject
    {
        private readonly ICameraService _cameraService;
        private BitmapSource? _currentImage;
        private int _frameCounter = 0;

        public ICameraService CameraService => _cameraService;

        public ImageSource? CameraImage
        {
            get => _currentImage;
            set
            {
                _currentImage = value as BitmapSource;
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        private string _cameraUrl = "0";

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private bool _isLiveStreaming;

        [ObservableProperty]
        private string _statusMessage = "Готов";

        [ObservableProperty]
        private string _cameraName;

        [ObservableProperty]
        private int _displayFps = 0;

        [ObservableProperty]
        private string _resolution = "N/A";

        [ObservableProperty]
        private ObservableCollection<CameraInfo> _availableCameras = new();

        [ObservableProperty]
        private CameraInfo? _selectedCamera;

        [ObservableProperty]
        private bool _showCameraList = true;

        [ObservableProperty]
        private bool _showUrlInput = false;

        private DateTime _lastFpsUpdate = DateTime.Now;
        private int _framesSinceLastUpdate = 0;

        public CameraViewModel(ICameraService cameraService, string cameraName)
        {
            _cameraService = cameraService;
            CameraName = cameraName;

            _cameraService.FrameCaptured += OnFrameCaptured;

            // Загружаем список камер при создании
            LoadAvailableCameras();
        }

        [RelayCommand]
        public async Task ConnectAsync()
        {
            StatusMessage = $"Подключение {CameraName}...";
            App.Log($"{CameraName}: Попытка подключения к {CameraUrl}");

            try
            {
                _cameraService.CameraUrl = CameraUrl;
                IsConnected = await _cameraService.ConnectAsync();

                if (IsConnected)
                {
                    StatusMessage = $"{CameraName}: Подключена успешно!";
                    Resolution = $"{_cameraService.FrameSize.Width}x{_cameraService.FrameSize.Height}";
                    DisplayFps = (int)_cameraService.FPS;
                    App.Log($"{CameraName}: Подключение успешно. Разрешение: {Resolution}, FPS: {DisplayFps}");
                }
                else
                {
                    StatusMessage = $"❌ {CameraName}: Не удалось подключиться";
                    App.Log($"{CameraName}: Подключение не удалось. Ошибка: {_cameraService.LastError}");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка подключения {CameraName}: {ex.Message}";
                IsConnected = false;
                App.Log($"{CameraName}: Ошибка подключения - {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task DisconnectAsync()
        {
            StatusMessage = $"Отключение {CameraName}...";

            await _cameraService.DisconnectAsync();
            IsConnected = false;
            IsLiveStreaming = false;
            CameraImage = null;
            DisplayFps = 0;
            Resolution = "N/A";

            StatusMessage = $"{CameraName} отключена";
            App.Log($"{CameraName}: Отключена");
        }

        [RelayCommand]
        public async Task StartLiveStreamAsync()
        {
            if (!IsConnected)
            {
                StatusMessage = $"Сначала подключите {CameraName}";
                return;
            }

            try
            {
                StatusMessage = $"Запуск live {CameraName}...";
                await _cameraService.StartLiveStream();
                IsLiveStreaming = true;
                StatusMessage = $"{CameraName}: LIVE ✓";
                App.Log($"{CameraName}: Live-трансляция запущена");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка запуска {CameraName}: {ex.Message}";
                App.Log($"{CameraName}: Ошибка запуска live - {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task StopLiveStreamAsync()
        {
            try
            {
                StatusMessage = $"Остановка {CameraName}...";
                await _cameraService.StopLiveStream();
                IsLiveStreaming = false;
                StatusMessage = $"{CameraName} остановлена";
                _frameCounter = 0;
                _framesSinceLastUpdate = 0;
                App.Log($"{CameraName}: Live-трансляция остановлена");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка остановки {CameraName}: {ex.Message}";
                App.Log($"{CameraName}: Ошибка остановки live - {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task CaptureFrameAsync()
        {
            if (!IsConnected)
            {
                StatusMessage = $"Сначала подключите {CameraName}";
                return;
            }

            try
            {
                StatusMessage = $"Захват кадра {CameraName}...";
                var frame = await _cameraService.CaptureFrameAsync();
                if (frame != null && !frame.Empty())
                {
                    CameraImage = ConvertMatToBitmapSource(frame);
                    StatusMessage = $"Кадр захвачен с {CameraName}";
                    _frameCounter++;
                    App.Log($"{CameraName}: Тестовый кадр захвачен {frame.Width}x{frame.Height}");
                }
                else
                {
                    StatusMessage = $"Не удалось захватить кадр с {CameraName}";
                    App.Log($"{CameraName}: Пустой кадр");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка захвата с {CameraName}: {ex.Message}";
                App.Log($"{CameraName}: Ошибка захвата кадра - {ex.Message}");
            }
        }

        [RelayCommand]
        public void LoadAvailableCameras()
        {
            try
            {
                AvailableCameras.Clear();
                var cameras = _cameraService.GetAvailableCameras();

                foreach (var camera in cameras)
                {
                    AvailableCameras.Add(camera);
                }

                // Выбираем первую камеру по умолчанию, если есть
                if (AvailableCameras.Any(c => c.Index == 0))
                {
                    SelectedCamera = AvailableCameras.First(c => c.Index == 0);
                }
                else if (AvailableCameras.Any())
                {
                    SelectedCamera = AvailableCameras.First();
                }

                App.Log($"Загружено {AvailableCameras.Count} камер");
            }
            catch (Exception ex)
            {
                App.Log($"Ошибка загрузки списка камер: {ex.Message}");
            }
        }

        partial void OnSelectedCameraChanging(CameraInfo? value)
        {
            if (value != null)
            {
                if (value.Index == -1) // IP-камера
                {
                    ShowCameraList = false;
                    ShowUrlInput = true;
                    CameraUrl = "rtsp://";
                }
                else // Локальная камера
                {
                    ShowCameraList = true;
                    ShowUrlInput = false;
                    CameraUrl = value.Index.ToString();
                }
            }
        }

        [RelayCommand]
        public void SwitchToUrlInput()
        {
            ShowCameraList = false;
            ShowUrlInput = true;
        }

        [RelayCommand]
        public void SwitchToCameraList()
        {
            ShowCameraList = true;
            ShowUrlInput = false;
            LoadAvailableCameras();
        }

        public async Task<Mat> GetFrameAsync()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Камера не подключена");

            return await _cameraService.CaptureFrameAsync();
        }

        private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (e.Frame != null && !e.Frame.Empty())
                        {
                            CameraImage = ConvertMatToBitmapSource(e.Frame);

                            // Обновляем счетчик FPS
                            _frameCounter++;
                            _framesSinceLastUpdate++;

                            var now = DateTime.Now;
                            var elapsed = (now - _lastFpsUpdate).TotalSeconds;

                            if (elapsed >= 1.0)
                            {
                                DisplayFps = (int)(_framesSinceLastUpdate / elapsed);
                                _framesSinceLastUpdate = 0;
                                _lastFpsUpdate = now;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Log($"{CameraName}: Ошибка обработки кадра в UI - {ex.Message}");
                    }
                }, System.Windows.Threading.DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                App.Log($"{CameraName}: Ошибка в OnFrameCaptured - {ex.Message}");
            }
        }

        private BitmapSource ConvertMatToBitmapSource(Mat mat)
        {
            try
            {
                if (mat == null || mat.Empty() || mat.Width == 0 || mat.Height == 0)
                {
                    App.Log($"Пустой кадр для конвертации");
                    return CreateBlackBitmap(640, 480);
                }

                // Конвертируем Mat в Bitmap
                using var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);

                // Конвертируем Bitmap в BitmapSource
                var hBitmap = bitmap.GetHbitmap();

                try
                {
                    var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        System.Windows.Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    bitmapSource.Freeze(); // Замораживаем для использования в UI потоке
                    return bitmapSource;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch (Exception ex)
            {
                App.Log($"Ошибка конвертации изображения: {ex.Message}");
                return CreateErrorBitmap(640, 480);
            }
        }

        private BitmapSource CreateBlackBitmap(int width, int height)
        {
            var stride = (width * 3 + 3) & ~3;
            var pixels = new byte[height * stride];
            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
        }

        private BitmapSource CreateErrorBitmap(int width, int height)
        {
            var stride = (width * 3 + 3) & ~3;
            var pixels = new byte[height * stride];

            // Красное изображение для ошибки
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i] = 0;     // B
                pixels[i + 1] = 0; // G
                pixels[i + 2] = 255; // R
            }

            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}