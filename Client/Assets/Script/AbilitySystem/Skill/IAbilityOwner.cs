using ProjectT.Stat;

namespace ProjectT.Skill
{
    public interface IAbilityOwner
    {
        SkillAgent SkillAgent { get; }
        Status Status { get; }
        // 효과가 잡아 둔 시전자가 반납·재스폰·파괴되었는지 이 값의 변화로 판단하므로 스폰 시작과 종료(파괴 포함)마다 바뀌어야 한다.
        int SpawnSerial { get; }
    }
}
