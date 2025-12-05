using Kamera21.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kamera21.Services
{
    public class InspectionService
    {
        private readonly ImageProcessor _imageProcessor;
        private readonly ComponentDetector _componentDetector;

        private const double GOOD_SOLDER_THRESHOLD = 0.7;
        private const double POOR_SOLDER_THRESHOLD = 0.4;
        private const double MIN_COMPONENT_CONFIDENCE = 0.6;

        public InspectionService(ImageProcessor imageProcessor)
        {
            _imageProcessor = imageProcessor;
            _componentDetector = new ComponentDetector();
        }

        public async Task<InspectionResult> InspectBoardAsync(Mat boardImage)
        {
            var result = new InspectionResult();

            try
            {
                using (var processed = _imageProcessor.PreprocessImage(boardImage))
                {
                    result.Components = await DetectComponentsAsync(processed);

                    var solderPads = _imageProcessor.FindSolderPads(processed);

                    var solderDefects = await InspectSolderPointsAsync(boardImage, solderPads);
                    result.Defects.AddRange(solderDefects);

                    var componentDefects = await InspectComponentsAsync(boardImage, result.Components);
                    result.Defects.AddRange(componentDefects);

                    var bridgeDefects = await FindSolderBridgesAsync(boardImage, solderPads);
                    result.Defects.AddRange(bridgeDefects);
                }

                result.IsSuccess = true;
                result.Message = $"Найдено {result.Defects.Count} дефектов";
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"Ошибка проверки: {ex.Message}";
                result.Error = ex;
            }

            return result;
        }

        private async Task<List<Component>> DetectComponentsAsync(Mat image)
        {
            return await Task.Run(() =>
            {
                var components = new List<Component>();

                var detected = _componentDetector.DetectComponents(image);

                foreach (var det in detected)
                {
                    var component = new Component
                    {
                        Designator = $"C{components.Count + 1}",
                        Type = DetermineComponentType(det),
                        BoundingBox = det.BoundingRect,
                        DetectionConfidence = det.Confidence
                    };

                    components.Add(component);
                }

                return components;
            });
        }

        private async Task<List<Defect>> InspectSolderPointsAsync(Mat image, List<Rect> solderPads)
        {
            return await Task.Run(() =>
            {
                var defects = new List<Defect>();

                foreach (var pad in solderPads)
                {
                    using (var roi = new Mat(image, pad))
                    {
                        var quality = _imageProcessor.AnalyzeSolderQuality(roi);

                        if (quality.IsPoor || !quality.IsGood)
                        {
                            var defectType = quality.IsPoor ? DefectType.PoorSolder : DefectType.ColdSolder;
                            var confidence = 1.0 - quality.OverallScore;

                            var defect = new Defect(
                                type: defectType,
                                location: new Point(pad.X + pad.Width / 2, pad.Y + pad.Height / 2),
                                confidence: confidence)
                            {
                                BoundingBox = pad,
                                Description = GetSolderDefectDescription(defectType, quality)
                            };

                            defects.Add(defect);
                        }

                        if (IsPadEmpty(roi))
                        {
                            var defect = new Defect(
                                type: DefectType.MissingSolder,
                                location: new Point(pad.X + pad.Width / 2, pad.Y + pad.Height / 2),
                                confidence: 0.9)
                            {
                                BoundingBox = pad,
                                Description = "Отсутствие пайки на контактной площадке"
                            };

                            defects.Add(defect);
                        }
                    }
                }

                return defects;
            });
        }

        private async Task<List<Defect>> InspectComponentsAsync(Mat image, List<Component> components)
        {
            return await Task.Run(() =>
            {
                var defects = new List<Defect>();

                foreach (var component in components)
                {
                    if (component.DetectionConfidence < MIN_COMPONENT_CONFIDENCE)
                    {
                        var defect = new Defect(
                            type: DefectType.MissingComponent,
                            location: component.Center,
                            confidence: 1.0 - component.DetectionConfidence)
                        {
                            BoundingBox = component.BoundingBox,
                            Description = $"Низкая уверенность детектирования компонента: {component.Designator}"
                        };

                        defects.Add(defect);
                        component.Defects.Add(defect);
                    }

                    if (IsComponentMisaligned(component, image))
                    {
                        var defect = new Defect(
                            type: DefectType.ComponentShift,
                            location: component.Center,
                            confidence: 0.8)
                        {
                            BoundingBox = component.BoundingBox,
                            Description = $"Компонент смещён: {component.Designator}"
                        };

                        defects.Add(defect);
                        component.Defects.Add(defect);
                    }
                }

                return defects;
            });
        }

        private async Task<List<Defect>> FindSolderBridgesAsync(Mat image, List<Rect> solderPads)
        {
            return await Task.Run(() =>
            {
                var defects = new List<Defect>();

                if (solderPads.Count < 2) return defects;

                using (var binary = new Mat())
                {
                    Cv2.CvtColor(image, binary, ColorConversionCodes.BGR2GRAY);
                    Cv2.Threshold(binary, binary, 100, 255, ThresholdTypes.BinaryInv);

                    Cv2.FindContours(binary, out var contours, out var hierarchy,
                        RetrievalModes.List, ContourApproximationModes.ApproxSimple);

                    foreach (var contour in contours)
                    {
                        var area = Cv2.ContourArea(contour);
                        if (area < 50) continue;

                        var boundingRect = Cv2.BoundingRect(contour);

                        var intersectingPads = solderPads
                            .Where(p => boundingRect.IntersectsWith(p))
                            .ToList();

                        if (intersectingPads.Count >= 2)
                        {
                            var center = new Point(
                                boundingRect.X + boundingRect.Width / 2,
                                boundingRect.Y + boundingRect.Height / 2);

                            var defect = new Defect(
                                type: DefectType.SolderBridge,
                                location: center,
                                confidence: 0.85)
                            {
                                BoundingBox = boundingRect,
                                Description = $"Возможная перемычка припоя между {intersectingPads.Count} площадками"
                            };

                            defects.Add(defect);
                        }
                    }
                }

                return defects;
            });
        }

        private bool IsPadEmpty(Mat roi)
        {
            using (var gray = new Mat())
            {
                Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

                Cv2.MeanStdDev(gray, out var mean, out var stddev);

                return stddev.Val0 < 15;
            }
        }

        private bool IsComponentMisaligned(Component component, Mat image)
        {
            var ratio = (float)component.BoundingBox.Width / component.BoundingBox.Height;

            if (component.Type == ComponentType.Resistor ||
                component.Type == ComponentType.Capacitor)
            {
                return ratio < 1.5 && ratio > 0.67;
            }

            return false;
        }

        private ComponentType DetermineComponentType(ComponentDetection detection)
        {
            var ratio = (float)detection.BoundingRect.Width / detection.BoundingRect.Height;
            var area = detection.BoundingRect.Width * detection.BoundingRect.Height;

            if (ratio > 2.0 || ratio < 0.5)
            {
                return area < 500 ? ComponentType.Resistor : ComponentType.Capacitor;
            }
            else if (Math.Abs(ratio - 1.0) < 0.2)
            {
                return area > 1000 ? ComponentType.IntegratedCircuit : ComponentType.LED;
            }

            return ComponentType.Unknown;
        }

        private string GetSolderDefectDescription(DefectType type, SolderQuality quality)
        {
            return type switch
            {
                DefectType.PoorSolder => $"Некачественная пайка. Оценка: {quality.OverallScore:F2}",
                DefectType.ColdSolder => $"Холодная пайка. Блеск: {quality.ShineScore:F2}",
                DefectType.MissingSolder => "Отсутствие пайки",
                _ => "Дефект пайки"
            };
        }
    }

    public class InspectionResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Error { get; set; }
        public List<Defect> Defects { get; set; } = new();
        public List<Component> Components { get; set; } = new();
        public DateTime InspectionTime { get; } = DateTime.Now;
        public TimeSpan ProcessingTime { get; set; }
    }
}