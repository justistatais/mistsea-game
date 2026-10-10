using System.Numerics;

namespace Mist.Core.Combat;

public static class CombatMath
{
    public static int Damage(int powerUnits,int coefficientBp,int damageBonusBp,int exposureBp,int armorBp,int guardBp)
    {
        if(new[]{powerUnits,coefficientBp,damageBonusBp,exposureBp,armorBp,guardBp}.Any(x=>x<0))throw new InvalidDataException("Negative combat quantity.");
        BigInteger numerator=(BigInteger)powerUnits*coefficientBp*(10000+Math.Min(5000,damageBonusBp))*(10000+Math.Min(4000,exposureBp))*(10000-Math.Min(4500,armorBp))*(10000-Math.Min(3500,guardBp));
        return checked((int)(numerator/(10*BigInteger.Pow(10000,5))));
    }
    public static int Shield(int powerUnits,int coefficientBp,int bonusBp)
    {
        if(powerUnits<0||coefficientBp<0||bonusBp<0)throw new InvalidDataException("Negative shield quantity.");
        return checked((int)((BigInteger)powerUnits*coefficientBp*(10000+Math.Min(5000,bonusBp))/(10*BigInteger.Pow(10000,2))));
    }
    public static int Pressure(int declared,int flat,int strength)
    {
        if(declared<0||flat<0||strength is <5 or >25)throw new InvalidDataException("Invalid pressure source.");
        return Math.Min(6,checked((declared+flat)*(100+2*(strength-5)))/100);
    }
}
