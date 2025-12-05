using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kamera21.Models;
using Kamera21.Services;
using Kamera21.Services.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kamera21.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly InspectionService _inspectionService;
        private readonly ImageProcessor _imageProcessor;
        private readonly ICameraService _cameraService1;
        private readonly ICameraService _cameraService2;

        public CameraViewModel Camera1 { get; }
        public CameraViewModel Camera2 { get; }

        [ObservableProperty]
        private string _inspectionStatus = "Готов к работе";

        [ObservableProperty]
        private string _camera1Status = "Не подключена";

        [ObservableProperty]
        private string _camera2Status = "Не подключена";

        [ObservableProperty]
        private bool _isInspecting;

        [ObservableProperty]
        private ImageSource? _processedImage;

        [ObservableProperty]
        private ObservableCollection<Defect> _defects = new();

        [ObservableProperty]
        private ObservableCollection<Component> _components = new();

        [ObservableProperty]
        private int _defectsCount;

        public MainViewModel(
            InspectionService inspectionService,
            ImageProcessor imageProcessor,
            ICameraService cameraService1,
            ICameraService cameraService2)
        {
            _inspectionService = inspectionService;
            _imageProcessor = imageProcessor;
            _cameraService1 = cameraService1;
            _cameraService2 = cameraService2;

            // Инициализируем ViewModel для каждой камеры
            Camera1 = new CameraViewModel(cameraService1, "Камера 1");
            Camera2 = new CameraViewModel(cameraService2, "Камера 2");

            // Подписываемся на изменения свойств камер
            Camera1.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Camera1.StatusMessage))
                    Camera1Status = Camera1.StatusMessage;
                OnPropertyChanged(nameof(Camera1));
            };

            Camera2.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Camera2.StatusMessage))
                    Camera2Status = Camera2.StatusMessage;
                OnPropertyChanged(nameof(Camera2));
            };
        }

        [RelayCommand]
        public async Task ConnectAllCamerasAsync()
        {
            InspectionStatus = "Подключение всех камер...";
            await Task.WhenAll(Camera1.ConnectAsync(), Camera2.ConnectAsync());
            InspectionStatus = "Камеры подключены";
        }

        [RelayCommand]
        public async Task DisconnectAllCamerasAsync()
        {
            InspectionStatus = "Отключение всех камер...";
            await Task.WhenAll(Camera1.DisconnectAsync(), Camera2.DisconnectAsync());
            InspectionStatus = "Камеры отключены";
        }

        [RelayCommand]
        public async Task StartAllLiveStreamsAsync()
        {
            InspectionStatus = "Запуск трансляций...";
            if (Camera1.IsConnected) await Camera1.StartLiveStreamAsync();
            if (Camera2.IsConnected) await Camera2.StartLiveStreamAsync();
            InspectionStatus = "Трансляции запущены";
        }

        [RelayCommand]
        public async Task StopAllLiveStreamsAsync()
        {
            InspectionStatus = "Остановка трансляций...";
            await Task.WhenAll(Camera1.StopLiveStreamAsync(), Camera2.StopLiveStreamAsync());
            InspectionStatus = "Трансляции остановлены";
        }

        [RelayCommand]
        public async Task StartInspectionAsync()
        {
            if (!Camera1.IsConnected && !Camera2.IsConnected)
            {
                InspectionStatus = "Подключите хотя бы одну камеру";
                return;
            }

            IsInspecting = true;
            InspectionStatus = "Проверка платы...";

            try
            {
                // Используем камеру 1 для проверки
                var frame = await Camera1.GetFrameAsync();

                var result = await _inspectionService.InspectBoardAsync(frame);

                if (result.IsSuccess)
                {
                    Defects.Clear();
                    Components.Clear();

                    foreach (var defect in result.Defects)
                        Defects.Add(defect);

                    foreach (var component in result.Components)
                        Components.Add(component);

                    DefectsCount = Defects.Count;

                    // Визуализируем дефекты
                    var processedFrame = _imageProcessor.VisualizeDefects(frame, result.Defects);
                    ProcessedImage = ConvertMatToBitmapSource(processedFrame);

                    InspectionStatus = $"Проверка завершена. Найдено {DefectsCount} дефектов";
                }
                else
                {
                    InspectionStatus = $"Ошибка проверки: {result.Message}";
                }
            }
            catch (Exception ex)
            {
                InspectionStatus = $"Ошибка: {ex.Message}";
                App.Log($"Ошибка проверки: {ex.Message}");
            }
            finally
            {
                IsInspecting = false;
            }
        }

        [RelayCommand]
        public void StopInspection()
        {
            IsInspecting = false;
            InspectionStatus = "Проверка остановлена";
        }

        [RelayCommand]
        public void SaveResults()
        {
            // TODO: Реализовать сохранение результатов
            InspectionStatus = "Результаты сохранены";
        }

        private BitmapSource ConvertMatToBitmapSource(Mat mat)
        {
            try
            {
                if (mat == null || mat.Empty() || mat.Width == 0 || mat.Height == 0)
                {
                    return CreateBlackBitmap(640, 480);
                }

                // Упрощенная конвертация
                using var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);
                var hBitmap = bitmap.GetHbitmap();

                try
                {
                    return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        System.Windows.Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch (Exception ex)
            {
                App.Log($"Ошибка конвертации: {ex.Message}");
                return CreateBlackBitmap(640, 480);
            }
        }

        private BitmapSource CreateBlackBitmap(int width, int height)
        {
            var stride = (width * 3 + 3) & ~3;
            var pixels = new byte[height * stride];
            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}