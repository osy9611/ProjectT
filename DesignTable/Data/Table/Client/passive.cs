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
    public class passiveInfo
    {
        
        [ProtoMember(1)]
        public short passive_Id;
        
        [ProtoMember(2)]
        public short status_Type;
        
        [ProtoMember(3)]
        public short status_Arg;
        
        [ProtoMember(4)]
        public string passive_name;
        
        [ProtoMember(5)]
        public string image_Res;
        
        public passiveInfo()
        {
        }
        
        public passiveInfo(short passive_Id, short status_Type, short status_Arg, string passive_name, string image_Res)
        {
		this.passive_Id = passive_Id;
		this.status_Type = status_Type;
		this.status_Arg = status_Arg;
		this.passive_name = passive_name;
		this.image_Res = image_Res;
        }
    }
    
    [ProtoContract()]
    public class passiveInfos
    {
        
        [ProtoMember(1)]
        public List<passiveInfo> dataInfo = new List<passiveInfo>();
        
        public Dictionary<ArraySegment<byte>, passiveInfo> datas = new Dictionary<ArraySegment<byte>, passiveInfo>(new DataComparer());
        
        public bool Insert(short passive_Id, short status_Type, short status_Arg, string passive_name, string image_Res)
        {
		foreach (passiveInfo info in dataInfo)
		{
			if(info.passive_Id == passive_Id)
			{
				return false;
			}
		}
			dataInfo.Add(new passiveInfo(passive_Id,status_Type,status_Arg,passive_name,image_Res));
			return true;
        }
        
        public void Initialize()
        {
		foreach (var data in dataInfo)
		{
			ArraySegment<byte> bytes = GetIdRule(data.passive_Id);
			if (datas.ContainsKey(bytes))
				continue;
			datas.Add(bytes, data);
		}
        }
        
        public passiveInfo Get(short passive_Id)
        {
		passiveInfo value = null;


		if (datas.TryGetValue(GetIdRule(passive_Id), out value))
			return value;
		return null;
        }
        
        public System.ArraySegment<byte> GetIdRule(short passive_Id)
        {
		ushort total = 0;
		ushort count = 0;
		total += sizeof(short);


		if( total == 0 )
			return default(System.ArraySegment<byte>);


		byte[] bytes = new byte[total];
		Array.Copy(BitConverter.GetBytes(passive_Id), 0, bytes, count, sizeof(short));
		count += sizeof(short);


		 return new System.ArraySegment<byte>(bytes);
        }
    }
}