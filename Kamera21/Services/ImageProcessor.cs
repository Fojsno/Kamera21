using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kamera21.Services
{
    public class ImageProcessor
    {
        private Mat _currentFrame = new();
        private readonly object _frameLock = new();

        public void UpdateFrame(Mat frame)
        {
            lock (_frameLock)
            {
                _currentFrame = frame.Clone();
            }
        }

        public Mat GetCurrentFrame()
        {
            lock (_frameLock)
            {
                return _currentFrame.Clone();
            }
        }

        public Mat PreprocessImage(Mat input, bool denoise = true, bool enhance = true)
        {
            var processed = input.Clone();

            if (processed.Channels() == 3)
            {
                Cv2.CvtColor(processed, processed, ColorConversionCodes.BGR2GRAY);
            }

            if (denoise)
            {
                Cv2.GaussianBlur(processed, processed, new Size(3, 3), 0);
            }

            if (enhance)
            {
                Cv2.EqualizeHist(processed, processed);
            }

            return processed;
        }

        public List<Rect> FindSolderPads(Mat image, int minArea = 50, int maxArea = 1000)
        {
            var pads = new List<Rect>();

            using (var processed = PreprocessImage(image))
            using (var binary = new Mat())
            {
                Cv2.Threshold(processed, binary, 0, 255, ThresholdTypes.Otsu);
                Cv2.BitwiseNot(binary, binary);

                Cv2.FindContours(binary, out var contours, out var hierarchy,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                foreach (var contour in contours)
                {
                    var area = Cv2.ContourArea(contour);
                    if (area >= minArea && area <= maxArea)
                    {
                        var rect = Cv2.BoundingRect(contour);
                        pads.Add(rect);
                    }
                }
            }

            return pads;
        }

        public SolderQuality AnalyzeSolderQuality(Mat roi)
        {
            var quality = new SolderQuality();

            if (roi.Empty() || roi.Width == 0 || roi.Height == 0)
                return quality;

            using (var hsv = new Mat())
            {
                if (roi.Channels() == 1)
                {
                    using var temp = new Mat();
                    Cv2.CvtColor(roi, temp, ColorConversionCodes.GRAY2BGR);
                    Cv2.CvtColor(temp, hsv, ColorConversionCodes.BGR2HSV);
                }
                else
                {
                    Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);
                }

                Cv2.Split(hsv, out var channels);
                using (var value = channels[2])
                using (var saturation = channels[1])
                {
                    var meanValue = Cv2.Mean(value)[0];
                    quality.ShineScore = meanValue / 255.0;

                    var meanSaturation = Cv2.Mean(saturation)[0];
                    quality.SaturationScore = 1.0 - (meanSaturation / 255.0);
                }
            }

            using (var gray = new Mat())
            {
                Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.Threshold(gray, gray, 0, 255, ThresholdTypes.Otsu);

                Cv2.FindContours(gray, out var contours, out var hierarchy,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                if (contours.Length > 0)
                {
                    var mainContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
                    quality.ContourArea = Cv2.ContourArea(mainContour);
                    quality.Perimeter = Cv2.ArcLength(mainContour, true);

                    if (quality.Perimeter > 0)
                    {
                        quality.Circularity = (4 * Math.PI * quality.ContourArea) /
                                             (quality.Perimeter * quality.Perimeter);
                    }

                    var hull = Cv2.ConvexHull(mainContour, true);
                    var hullArea = Cv2.ContourArea(hull);
                    quality.Convexity = hullArea > 0 ? quality.ContourArea / hullArea : 0;
                }
            }

            quality.OverallScore = (quality.ShineScore * 0.4 +
                                   quality.SaturationScore * 0.2 +
                                   quality.Circularity * 0.2 +
                                   quality.Convexity * 0.2);

            return quality;
        }

        public Mat VisualizeDefects(Mat image, List<Models.Defect> defects)
        {
            var result = image.Clone();

            foreach (var defect in defects)
            {
                Scalar color = defect.Severity switch
                {
                    Models.DefectSeverity.Low => Scalar.Yellow,
                    Models.DefectSeverity.Medium => Scalar.Orange,
                    Models.DefectSeverity.High => Scalar.Red,
                    Models.DefectSeverity.Critical => Scalar.DarkRed,
                    _ => Scalar.White
                };

                Cv2.Rectangle(result, defect.BoundingBox, color, 2);
                Cv2.Circle(result, defect.Location, 5, color, -1);

                var label = $"{defect.Type} ({defect.Confidence:P0})";
                Cv2.PutText(result, label,
                    new Point(defect.BoundingBox.X, defect.BoundingBox.Y - 5),
                    HersheyFonts.HersheySimplex, 0.5, color, 1);
            }

            return result;
        }
    }

    public class SolderQuality
    {
        public double ShineScore { get; set; }
        public double SaturationScore { get; set; }
        public double Circularity { get; set; }
        public double Convexity { get; set; }
        public double ContourArea { get; set; }
        public double Perimeter { get; set; }
        public double OverallScore { get; set; }

        public bool IsGood => OverallScore >= 0.7;
        public bool IsPoor => OverallScore < 0.4;
    }
}