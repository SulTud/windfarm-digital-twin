namespace WindFarm.Simulation
{
    /// <summary>
    /// Türbinin denetim sistemi (supervisory controller) tarafından belirlenen çalışma durumu.
    /// </summary>
    public enum TurbineOperatingState
    {
        /// <summary>Rüzgar cut-in hızının altında; jeneratör şebekeden ayrık, rotor duruyor.</summary>
        Idle,

        /// <summary>Kısmi yük bölgesi; rotor rüzgarı optimum uç hız oranıyla takip ediyor.</summary>
        Producing,

        /// <summary>Nominal güce ulaşıldı; kanat açısı (pitch) kontrolü gücü sabit tutuyor.</summary>
        RatedPower,

        /// <summary>Rüzgar cut-out hızını aştı; kanatlar tüylendi (feather), rotor frenleniyor.</summary>
        StormShutdown
    }
}
