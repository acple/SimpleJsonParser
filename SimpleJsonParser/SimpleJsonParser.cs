using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SimpleJsonParser
{
    [Flags]
    public enum JsonType
    { Number = 1, String = 1 << 1, Array = 1 << 2, Object = 1 << 3, Bool = 1 << 4, Null = 1 << 5, Undefined = 0 }

    [DebuggerDisplay("{ToString(),nq}", Name = "{Name}", Type = "JsonElement.{Type}")]
    [DebuggerTypeProxy(typeof(JsonElementDebugView))]
    public abstract class JsonElement : IEnumerable<JsonElement>
    {
        public string Name { get; }
        public JsonType Type { get; }

        public int Int => checked((int)ParseToLong(GetValue<string>(JsonType.Number), false));
        public long Long => ParseToLong(GetValue<string>(JsonType.Number), false);
        public double Double => ParseToDouble(GetValue<string>(JsonType.Number));
        public string String => GetValue<string>(JsonType.String);
        public bool Bool => GetValue<bool>(JsonType.Bool);

        public int IntForce => (int)ParseToLong(GetValue<string>(JsonType.Number), true);
        public long LongForce => ParseToLong(GetValue<string>(JsonType.Number), true);

        public JsonElement this[string key]
        {
            get
            {
                JsonElement element;
                if (GetValue<IDictionary<string, JsonElement>>(JsonType.Object).TryGetValue(key, out element)) return element;
                else throw new KeyNotFoundException($"要素名 \"{key}\" が [{this.Name}] に存在しません");
            }
        }

        public JsonElement this[int index] => GetValue<IList<JsonElement>>(JsonType.Array)[index];

        public bool ContainsKey(string key) => GetValue<IDictionary<string, JsonElement>>(JsonType.Object).ContainsKey(key);

        public bool TypeIs(JsonType type) => 0 != (this.Type & type);

        private T GetValue<T>(JsonType type)
        {
            if (TypeIs(type)) return (this as Element<T>).Value;
            else throw new InvalidOperationException($"要素 \"{this.Name}\" は <{type.ToString()}> 型ではありません");
        }

        private static long ParseToLong(string value, bool force)
        {
            long l;
            if (long.TryParse(value, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out l)) return l;
            else if (force) return (long)ParseToDouble(value);
            else throw new ArgumentException($"{typeof(long)} 型へのパースに失敗しました value=[{value}]");
        }

        private static double ParseToDouble(string value)
        {
            double d;
            if (double.TryParse(value, NumberStyles.Float, NumberFormatInfo.InvariantInfo, out d)) return d;
            else throw new ArgumentException($"numberのパースに失敗しました value=[{value}]");
        }

        private static string GetElementName(XElement element) => element.Attribute("item")?.Value ?? element.Name.LocalName;

        private static IDictionary<string, JsonElement> JsonObject(IEnumerable<XElement> elements)
        {
            var name = null as string;
            return elements.ToDictionary(x => name = GetElementName(x), x => CreateJsonElement(x, name));
        }

        private static IList<JsonElement> JsonArray(IEnumerable<XElement> elements) => elements
            .Select((x, i) => CreateJsonElement(x, i.ToString()))
            .ToList();

        private static JsonElement CreateJsonElement(XElement element) => CreateJsonElement(element, GetElementName(element));
        private static JsonElement CreateJsonElement(XElement element, string name)
        {
            switch (element.Attribute("type").Value)
            {
                case "number": return new Element<string>(element.Value, name, JsonType.Number);
                case "string": return new Element<string>(element.Value, name, JsonType.String);
                case "array": return new Element<IList<JsonElement>>(JsonArray(element.Elements()), name, JsonType.Array);
                case "object": return new Element<IDictionary<string, JsonElement>>(JsonObject(element.Elements()), name, JsonType.Object);
                case "boolean": return new Element<bool>(bool.Parse(element.Value), name, JsonType.Bool);
                case "null": return new Element<object>(null, name, JsonType.Null);
                default: throw new ArgumentException("JsonTypeの判別に失敗しました");
            }
        }

        protected JsonElement(string name, JsonType type)
        {
            this.Type = type;
            this.Name = name;
        }

        private class Element<T> : JsonElement
        {
            public T Value { get; }

            public Element(T value, string name, JsonType type) : base(name, type)
            {
                this.Value = value;
            }

            private static string CreateEscapedString(string str) =>
                "\"" + str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r")
                    .Replace("\t", "\\t").Replace("\f", "\\f").Replace("\b", "\\b").Replace("/", "\\/") + "\"";

            public override string ToString()
            {
                switch (this.Type)
                {
                    case JsonType.Number: return this.Value as string;
                    case JsonType.String: return CreateEscapedString(this.Value as string);
                    case JsonType.Array: return "[" + string.Join(",", this.Select(x => x.ToString())) + "]";
                    case JsonType.Object: return "{" + string.Join(",", this.Select(x => CreateEscapedString(x.Name) + ":" + x.ToString())) + "}";
                    case JsonType.Bool: return this.Value.ToString().ToLower();
                    case JsonType.Null: return "null";
                    default: return null;
                }
            }
        }

        public IEnumerator<JsonElement> GetEnumerator()
        {
            switch (this.Type)
            {
                case JsonType.Array: return GetValue<IList<JsonElement>>(JsonType.Array).GetEnumerator();
                case JsonType.Object: return GetValue<IDictionary<string, JsonElement>>(JsonType.Object).Values.GetEnumerator();
                default: return Enumerable.Empty<JsonElement>().GetEnumerator();
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();

        public static JsonElement Parse(string json) => Parse(json, Encoding.UTF8);
        public static JsonElement Parse(string json, Encoding encoding)
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(encoding.GetBytes(json), XmlDictionaryReaderQuotas.Max))
                return CreateJsonElement(XElement.Load(reader));
        }

        public static JsonElement Parse(Stream stream) => Parse(stream, Encoding.UTF8);
        public static JsonElement Parse(Stream stream, Encoding encoding)
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(stream, encoding, XmlDictionaryReaderQuotas.Max, null))
                return CreateJsonElement(XElement.Load(reader));
        }

        private class JsonElementDebugView
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public JsonElement[] ChildNodes { get; }

            private JsonElementDebugView(JsonElement element)
            {
                this.ChildNodes = (element.TypeIs(JsonType.Array | JsonType.Object)) ? element.ToArray() : null;
            }
        }
    }
}
