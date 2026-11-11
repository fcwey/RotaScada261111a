using Core.Entities;
using System.Text.Json.Serialization;

namespace Entities.Concrete
{
    // Reçete adımı (DB202 / DB203 içindeki 28 baytlık adım yapısı)
    public class RecipeStep : IEntity
    {
        public int Id { get; set; }
        public int RecipeId { get; set; }
        public int StepNo { get; set; }             // Adım sırası, 1–20
        public int StepType { get; set; }           // 1 vakum, 2 ısıtma, 3 soğutma

        public int Vac_SetPoint { get; set; }       // mbar (atmosfer 1000, tam vakum 0)
        public int Vac_Timeout { get; set; }        // dk, en az 1

        public int TargetTemp { get; set; }         // °C; ısıtmada hedef, soğutmada bitiş sıcaklığı
        public int StepTime { get; set; }           // dk, en az 1
        public int GSoak { get; set; }              // °C, 0 = kapalı

        public int N2_Flow { get; set; }            // L/saat
        public int NH3_Flow { get; set; }           // L/saat
        public int dNH3_Flow { get; set; }          // L/saat, şimdilik hep 0
        public int CO2_Perc { get; set; }           // %, 0–100
        public int Su_On { get; set; }              // 0/1
        public int H2_Auto { get; set; }            // 0/1
        public int H2_Set { get; set; }             // % x10 (325 = %32,5)
        public int FanSpeed { get; set; }           // %, 0 = varsayılan hız

        // JSON çıktısında döngü oluşmasın diye gizlenir
        [JsonIgnore]
        public virtual Recipe? Recipe { get; set; }
    }
}