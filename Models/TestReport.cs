namespace MailDataImporter.Models;

public sealed class TestReport
{
    public TestReport(string? grade, string? lot, string? blankLot, string? rtpLot, string? partNumber, int qty, decimal hardness, decimal density, int? coercivity, int? magneticSaturation, float? trs, string? porosity, decimal grainSize, string? pitch, DateOnly shipDate, string? shipMethod, string supplier)
    {
        Grade = grade;
        LotNumber = lot;
        BlankLotNumber = blankLot;
        RtpLotNumber = rtpLot;
        PartNumber = partNumber;
        Quantity = qty;
        Hardness = hardness;
        Density = density;
        Coercivity = coercivity;
        MagneticSaturation = magneticSaturation;
        TransverseRuptureStrength = trs;
        Porosity = porosity;
        GrainSize = grainSize;
        Pitch = pitch;
        ShipDate = shipDate;
        ShipMethod = shipMethod;
        Supplier = supplier;
    }
    
    public string? Grade { get; init; }
    public string? LotNumber { get; init; }
    public string? BlankLotNumber { get; init; }
    public string? RtpLotNumber { get; init; }
    public string? PartNumber { get; init; }
    public int Quantity { get; init; }
    public decimal Hardness { get; init; }
    public decimal Density { get; init; }
    public int? Coercivity { get; init; }
    public int? MagneticSaturation { get; init; }
    public float? TransverseRuptureStrength { get; init; }
    public string? Porosity { get; init; }
    public decimal GrainSize { get; init; }
    public string? Pitch { get; init; }
    public DateOnly ShipDate { get; init; }
    public string? ShipMethod { get; init; }
    public string Supplier { get; init; }
}