using Core.Entities;

namespace Entities.Dtos
{
    // Parametre ekranında gösterilen satır: tanım ve PLC'deki güncel değer
    public class ParamValueDto : IDto
    {
        public int Address { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Default { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public bool IsSafety { get; set; }
        public int Value { get; set; }
    }
}