using Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Entities.Concrete
{
    // Reçete başlığı (DB202 / DB203 başlık alanları). Id, PLC'ye RecipeID olarak gönderilir.
    public class Recipe : IEntity
    {
        public int Id { get; set; }
        public int RecipeType { get; set; }                 // 1 meneviş, 2 nitrasyon, 3 oksidasyon
        public string RecipeName { get; set; } = string.Empty; // En fazla 30 karakter
        public int TotalStepCount { get; set; }             // Kullanılan adım sayısı, 1–20

        public int PressureSetPoint { get; set; }           // mbar
        public int PressureMode { get; set; }               // Sadece menevişte: 0 sürekli temizle, 10 atmosferi koru, 20 sıcaklığa bağlı
        public int PressureModeTime { get; set; }           // dk, atmosferi koru modunda tazeleme aralığı (0 = yok)
        public int PressureModeTemp { get; set; }           // °C, sıcaklığa bağlı modda geçiş sıcaklığı
        public int GsoakTcId { get; set; }                  // 0 pota, 1 malzeme, 2 ve 3 opsiyonel
        public int CO2_Period { get; set; }                 // dk, CO2 aç-kapa periyodu

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;

        public virtual ICollection<RecipeStep> Steps { get; set; } = new List<RecipeStep>();
    }
}