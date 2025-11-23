using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nexa.Shared.Models
{
    public class HealthResponse
    {
        public string Status { get; set; } = string.Empty;
        public List<HealthCheckItem> Checks { get; set; } = new();
        public double TotalDuration { get; set; }
    }

    public class HealthCheckItem
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Duration { get; set; }
    }
}