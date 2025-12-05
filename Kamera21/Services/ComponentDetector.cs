using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kamera21.Services
{
    public class ComponentDetection
    {
        public Rect BoundingRect { get; set; }
        public double Confidence { get; set; }
        public Models.ComponentType Type { get; set; }
    }

    public class ComponentDetector
    {
        public List<ComponentDetection> DetectComponents(Mat image)
        {
            var detections = new List<ComponentDetection>();

            using (var gray = new Mat())
            {
                if (image.Channels() == 3)
                    Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                else
                    image.CopyTo(gray);

                using (var equalized = new Mat())
                {
                    Cv2.EqualizeHist(gray, equalized);

                    using (var binary = new Mat())
                    {
                        Cv2.Threshold(equalized, binary, 0, 255, ThresholdTypes.Otsu);
                        Cv2.BitwiseNot(binary, binary);

                        using (var kernel = Cv2.GetStructuringElement(
                            MorphShapes.Rect, new Size(3, 3)))
                        {
                            Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel);
                            Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel);

                            Cv2.FindContours(binary, out var contours, out var hierarchy,
                                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                            var minArea = 100;
                            var maxArea = 10000;

                            foreach (var contour in contours)
                            {
                                var area = Cv2.ContourArea(contour);
                                if (area < minArea || area > maxArea)
                                    continue;

                                var boundingRect = Cv2.BoundingRect(contour);

                                var ratio = (float)boundingRect.Width / boundingRect.Height;
                                if (ratio > 5 || ratio < 0.2)
                                    continue;

                                var perimeter = Cv2.ArcLength(contour, true);
                                var circularity = 4 * Math.PI * area / (perimeter * perimeter);
                                var confidence = Math.Clamp(circularity * 0.8 + 0.2, 0.1, 1.0);

                                var componentType = ClassifyComponent(boundingRect);

                                detections.Add(new ComponentDetection
                                {
                                    BoundingRect = boundingRect,
                                    Confidence = confidence,
                                    Type = componentType
                                });
                            }
                        }
                    }
                }
            }

            return detections;
        }

        private Models.ComponentType ClassifyComponent(Rect boundingRect)
        {
            var area = boundingRect.Width * boundingRect.Height;
            var ratio = (float)boundingRect.Width / boundingRect.Height;

            if (ratio > 1.8 || ratio < 0.55)
            {
                if (area < 500) return Models.ComponentType.Resistor;
                if (area < 2000) return Models.ComponentType.Capacitor;
                return Models.ComponentType.Inductor;
            }

            if (area < 400) return Models.ComponentType.LED;
            if (area < 1000) return Models.ComponentType.Diode;
            if (area < 2500) return Models.ComponentType.Transistor;

            return Models.ComponentType.IntegratedCircuit;
        }
    }
}