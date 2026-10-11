using ProjectT.Stat;

namespace ProjectT.Skill
{
    // 데미지 공식이 정해지지 않아 계산을 교체할 수 있게 둔다. source는 시전자가 없거나, 적용 뒤 반납·재스폰·파괴되었거나, Status가 없으면 null이다.
    // 적용 시점에 이미 사라진 시전자는 판별할 수 없으므로 호출자가 null을 넘긴다.
    public abstract class DamageExecution
    {
        public abstract float Execute(Status source, Status target, float power);
    }

    public sealed class FlatDamageExecution : DamageExecution
    {
        public override float Execute(Status source, Status target, float power) => power;
    }
}
