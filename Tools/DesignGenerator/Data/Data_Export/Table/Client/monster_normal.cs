//------------------------------------------------------------------------------
// <auto-generated>
//     �� �ڵ�� ������ ����Ͽ� �����Ǿ����ϴ�.
//     ��Ÿ�� ����:4.0.30319.42000
//
//     ���� ������ �����ϸ� �߸��� ������ �߻��� �� ������, �ڵ带 �ٽ� �����ϸ�
//     �̷��� ���� ������ �սǵ˴ϴ�.
// </auto-generated>
//------------------------------------------------------------------------------

namespace DesignTable
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using ProtoBuf;
    
    
    [ProtoContract()]
    public class monster_normalInfo
    {
        
        [ProtoMember(1)]
        public short mon_id;
        
        [ProtoMember(2)]
        public int mon_pathId;
        
        [ProtoMember(3)]
        public sbyte mon_type;
        
        [ProtoMember(4)]
        public short mon_spawnId;
        
        [ProtoMember(5)]
        public short mon_spawnTime;
        
        [ProtoMember(6)]
        public sbyte mon_spawnDayNight;
        
        public monster_normalInfo()
        {
        }
        
        public monster_normalInfo(short mon_id, int mon_pathId, sbyte mon_type, short mon_spawnId, short mon_spawnTime, sbyte mon_spawnDayNight)
        {
		this.mon_id = mon_id;
		this.mon_pathId = mon_pathId;
		this.mon_type = mon_type;
		this.mon_spawnId = mon_spawnId;
		this.mon_spawnTime = mon_spawnTime;
		this.mon_spawnDayNight = mon_spawnDayNight;
        }
    }
    
    [ProtoContract()]
    public class monster_normalInfos
    {
        
        [ProtoMember(1)]
        public List<monster_normalInfo> dataInfo = new List<monster_normalInfo>();
        
        public Dictionary<ArraySegment<byte>, monster_normalInfo> datas = new Dictionary<ArraySegment<byte>, monster_normalInfo>(new DataComparer());
        
        public bool Insert(short mon_id, int mon_pathId, sbyte mon_type, short mon_spawnId, short mon_spawnTime, sbyte mon_spawnDayNight)
        {
		foreach (monster_normalInfo info in dataInfo)
		{
			if(info.mon_id == mon_id)
			{
				return false;
			}
		}
			dataInfo.Add(new monster_normalInfo(mon_id,mon_pathId,mon_type,mon_spawnId,mon_spawnTime,mon_spawnDayNight));
			return true;
        }
        
        public void Initialize()
        {
		foreach (var data in dataInfo)
		{
			ArraySegment<byte> bytes = GetIdRule(data.mon_id);
			if (datas.ContainsKey(bytes))
				continue;
			datas.Add(bytes, data);
		}
        }
        
        public monster_normalInfo Get(short mon_id)
        {
		monster_normalInfo value = null;


		if (datas.TryGetValue(GetIdRule(mon_id), out value))
			return value;
		return null;
        }
        
        public System.ArraySegment<byte> GetIdRule(short mon_id)
        {
		ushort total = 0;
		ushort count = 0;
		total += sizeof(short);


		if( total == 0 )
			return default(System.ArraySegment<byte>);


		byte[] bytes = new byte[total];
		Array.Copy(BitConverter.GetBytes(mon_id), 0, bytes, count, sizeof(short));
		count += sizeof(short);


		 return new System.ArraySegment<byte>(bytes);
        }
    }
}
