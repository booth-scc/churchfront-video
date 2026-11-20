namespace CueVideo.Atem;

/// <summary>One ATEM source as reported by InPr.</summary>
public sealed record AtemInput(int Id, string Long, string Short)
{
    /// <summary>Black, external inputs, bars, colours, media players, SuperSource.</summary>
    public bool IsBusSource =>
        Id == 0 || Id is >= 1 and <= 999 || Id == 1000 || Id == 2001 || Id == 2002
        || (Id is >= 3000 and < 4000 && Id % 10 == 0) || Id is >= 6000 and < 6100;

    /// <summary>Aux outputs additionally accept clean feeds and M/E program/preview.</summary>
    public bool IsAuxSource => IsBusSource || Id is >= 7000 and < 7100 || Id is >= 10000 and < 11000;

    public bool IsExternal => Id is >= 1 and <= 999;

    internal (int Group, int Id) SortKey => IsExternal ? (0, Id) : (1, Id);
}
