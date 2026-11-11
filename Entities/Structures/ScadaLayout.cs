namespace Entities.Structures
{
    // PLC – SCADA arayüzünün (şema sürümü 3) DB numaraları, boyutları ve sabit adresleri
    public static class ScadaLayout
    {
        public const short LayoutVersion = 3;

        public const int DbCmd = 200;           // DB_ScadaCmd (SCADA yazar)
        public const int DbStatus = 201;        // DB_ScadaStatus (SCADA okur)
        public const int DbRecipe = 202;        // DB_ScadaRecipe (SCADA yazar)
        public const int DbActiveRecipe = 203;  // DB_ActiveRecipe (SCADA okur)
        public const int DbParam = 204;         // DB_ScadaParam (SCADA okur ve yazar)

        public const int CmdFrameSize = 10;         // DB200 adres 0–9: CmdId, CmdCode, Param1..3
        public const int CmdHeartbeatOffset = 10;   // DB200 adres 10: SCADA yaşam sinyali

        public const int StatusSize = 248;          // DB201 toplam boyut
        public const int StatusVersionOffset = 2;   // DB201 adres 2: LayoutVersion
        public const int StatusAckOffset = 4;       // DB201 adres 4: AckCmdId, AckResult, AckDetail
        public const int StatusAckSize = 6;

        public const int ParamVersionOffset = 0;    // DB204 adres 0: LayoutVersion

        public const int CommandTimeoutMs = 2000;   // Komut cevabı için en uzun bekleme
        public const int CommandPollMs = 50;        // Cevap beklerken okuma aralığı
    }
}