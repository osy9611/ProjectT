using ProjectT.Stat;

namespace ProjectT.Skill
{
    // 데미지 공식이 정해지지 않아 계산을 교체할 수 있게 둔다. source는 시전자가 없거나 사라졌거나 Status가 없으면 null이다.
    public abstract class DamageExecution
    {
        public abstract float Execute(Status source, Status target, float power);
    }

    public sealed class FlatDamageExecution : DamageExecution
    {
        public override float Execute(Status source, Status target, float power) => power;
    }
}
