﻿using System.Collections.Specialized;

namespace AssetStudio
{
    public class Object
    {
        public SerializedFile assetsFile;
        public ObjectReader reader;
        public long m_PathID;
        public int[] version;
        protected BuildType buildType;
        public BuildTarget platform;
        public ClassIDType type;
        public SerializedType serializedType;
        public uint byteSize;

        public Object(ObjectReader reader)
        {// SubReader
            this.reader = reader;
            reader.Reset();
            assetsFile = reader.assetsFile;
            type = reader.type;
            m_PathID = reader.m_PathID;
            version = reader.version;
            buildType = reader.buildType;
            platform = reader.platform;
            serializedType = reader.serializedType;
            byteSize = reader.byteSize;

            if (platform == BuildTarget.NoTarget)
            {
                var m_ObjectHideFlags = reader.ReadUInt32();
            }
        }
#if ORG
            public string Dump()
            {
                if (serializedType?.m_Type != null)
                {
                    return TypeTreeHelper.ReadTypeString(serializedType.m_Type, reader);
                }
                return null;
            }

            public string Dump(TypeTree m_Type)
            {
                if (m_Type != null)
                {
                    return TypeTreeHelper.ReadTypeString(m_Type, reader);
                }
                return null;
            }

            public OrderedDictionary ToType()
            {
                if (serializedType?.m_Type != null)
                {
                    return TypeTreeHelper.ReadType(serializedType.m_Type, reader);
                }
                return null;
            }

            public OrderedDictionary ToType(TypeTree m_Type)
            {
                if (m_Type != null)
                {
                    return TypeTreeHelper.ReadType(m_Type, reader);
                }
                return null;
            }
#endif

        public byte[] GetRawData()
        {
            reader.Reset();
            return reader.ReadBytes((int)byteSize);
        }
    }
}