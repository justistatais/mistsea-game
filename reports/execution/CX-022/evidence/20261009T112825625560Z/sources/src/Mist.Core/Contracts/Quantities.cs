namespace Mist.Core.Contracts;

public sealed record RewardBundle(int Experience, long Marks, int Parts, string Entitlement)
{
    public void Validate()
    {
        if (Experience < 0 || Marks < 0 || Parts < 0 || string.IsNullOrWhiteSpace(Entitlement)) throw new InvalidDataException("Invalid reward bundle.");
    }
}
public static class ExactMath
{
    public static int BasisPoints(int integerValue, int basisPoints) => checked((int)decimal.Floor(integerValue * (decimal)basisPoints / 10000m));
}
