using OpenCvSharp;
using System;

namespace Kamera21.Models
{
    public enum DefectType
    {
        None = 0,
        MissingSolder,
        PoorSolder,
        SolderBridge,
        ComponentShift,
        MissingComponent,
        WrongComponent,
        ColdSolder,
        ExcessSolder
    }

    public enum DefectSeverity
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    public class Defect
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DefectType Type { get; set; }
        public DefectSeverity Severity { get; set; }
        public Point Location { get; set; }
        public Rect BoundingBox { get; set; }
        public double Confidence { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime DetectedTime { get; } = DateTime.Now;

        public Defect(DefectType type, Point location, double confidence = 0.5)
        {
            Type = type;
            Location = location;
            Confidence = Math.Clamp(confidence, 0, 1);

            Severity = type switch
            {
                DefectType.SolderBridge => DefectSeverity.Critical,
                DefectType.MissingComponent => DefectSeverity.High,
                DefectType.MissingSolder => DefectSeverity.Medium,
                DefectType.PoorSolder => DefectSeverity.Medium,
                _ => DefectSeverity.Low
            };
        }
    }
}