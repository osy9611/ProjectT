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
    using DesignTable;
    using ProtoBuf;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    
    
    public sealed class DataMessageSerializer
    {
        
        public byte[] Serialize(object tableInfos)
        {
		System.IO.MemoryStream stream = new System.IO.MemoryStream();
		Serializer.Serialize(stream, tableInfos);
		byte[] buffer = stream.ToArray();

		return buffer;
        }
        
        public object Deserialize(int tableId, byte[] buffer)
        {
		System.IO.MemoryStream stream = new System.IO.MemoryStream(buffer);
		switch (tableId)
		{
		}
		return null;
        }
    }
}