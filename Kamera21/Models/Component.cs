using OpenCvSharp;
using System.Collections.Generic;

namespace Kamera21.Models
{
    public enum ComponentType
    {
        Unknown = 0,
        Resistor,
        Capacitor,
        Inductor,
        Diode,
        Transistor,
        IntegratedCircuit,
        Connector,
        Button,
        LED
    }

    public class Component
    {
        public string Designator { get; set; } = string.Empty;
        public ComponentType Type { get; set; }
        public Rect BoundingBox { get; set; }
        public Point Center => new(BoundingBox.X + BoundingBox.Width / 2,
                                  BoundingBox.Y + BoundingBox.Height / 2);
        public string Value { get; set; } = string.Empty;
        public bool IsDetected { get; set; } = true;
        public double DetectionConfidence { get; set; }
        public List<Defect> Defects { get; } = new();

        public Mat? TemplateImage { get; set; }
        public Mat? RegionOfInterest { get; set; }
    }
}