using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kamera21.Services.Interfaces
{
    public interface ICameraService : IDisposable
    {
        bool IsConnected { get; }
        int CameraIndex { get; set; }
        string CameraUrl { get; set; }
        Size FrameSize { get; set; }
        double FPS { get; }
        string LastError { get; }

        Task<bool> ConnectAsync();
        Task DisconnectAsync();
        Task<Mat> CaptureFrameAsync();

        // Методы для live-трансляции
        Task StartLiveStream();
        Task StopLiveStream();

        // Для совместимости со старым кодом
        Task StartContinuousCapture();
        Task StopContinuousCapture();

        // Новый метод: получение списка доступных камер
        List<CameraInfo> GetAvailableCameras();

        event FrameCapturedEventHandler FrameCaptured;
    }

    public delegate void FrameCapturedEventHandler(object sender, FrameCapturedEventArgs e);

    public class FrameCapturedEventArgs : EventArgs
    {
        public Mat Frame { get; }
        public DateTime Timestamp { get; }

        public FrameCapturedEventArgs(Mat frame)
        {
            Frame = frame.Clone();
            Timestamp = DateTime.Now;
        }
    }

    // Новый класс для информации о камере
    public class CameraInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public string DisplayName => $"Камера {Index}" + (string.IsNullOrEmpty(Name) ? "" : $" ({Name})");
    }
}