using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public string Name { get; private set; }
        public JsonType Type { get; private set; }

        public int Int { get { return checked((int)ParseToLong(GetValue<string>(JsonType.Number), false)); } }
        public long Long { get { return ParseToLong(GetValue<string>(JsonType.Number), false); } }
        public double Double { get { return ParseToDouble(GetValue<string>(JsonType.Number)); } }
        public string String { get { return GetValue<string>(JsonType.String); } }
        public bool Bool { get { return GetValue<bool>(JsonType.Bool); } }

        public int IntForce { get { return (int)ParseToLong(GetValue<string>(JsonType.Number), true); } }
        public long LongForce { get { return ParseToLong(GetValue<string>(JsonType.Number), true); } }

        public JsonElement this[string key]
        {
            get
            {
                try { return GetValue<IDictionary<string, JsonElement>>(JsonType.Object)[key]; }
                catch (KeyNotFoundException ex)
                { throw new KeyNotFoundException(string.Format("要素名 \"{0}\" が [{1}] に存在しません", key, this.Name), ex); }
            }
        }

        public JsonElement this[int index]
        {
            get { return GetValue<IList<JsonElement>>(JsonType.Array)[index]; }
        }

        public bool ContainsKey(string key)
        {
            return GetValue<IDictionary<string, JsonElement>>(JsonType.Object).ContainsKey(key);
        }

        public bool TypeIs(JsonType type)
        {
            return 0 != (this.Type & type);
        }

        private T GetValue<T>(JsonType type)
        {
            if (TypeIs(type))
                return (this as Element<T>).Value;
            throw new InvalidOperationException(string.Format("要素 \"{0}\" は <{1}> 型ではありません", this.Name, type.ToString()));
        }

        private static long ParseToLong(string value, bool force)
        {
            long l;
            if (long.TryParse(value, out l)) return l;
            else if (force) return (long)ParseToDouble(value);
            else throw new ArgumentException(typeof(long).ToString() + " 型へのパースに失敗しました");
        }

        private static double ParseToDouble(string value)
        {
            double d;
            if (double.TryParse(value, out d)) return d;
            else throw new ArgumentException("numberのパースに失敗しました");
        }

        private static string GetElementName(XElement element)
        {
            return element.Attribute("item") != null ? element.Attribute("item").Value : element.Name.LocalName;
        }

        private static IDictionary<string, JsonElement> JsonObject(IEnumerable<XElement> elements)
        {
            var name = null as string;
            return elements.ToDictionary(x => name = GetElementName(x), x => CreateJsonElement(x, name));
        }

        private static IList<JsonElement> JsonArray(IEnumerable<XElement> elements)
        {
            return elements.Select((x, i) => CreateJsonElement(x, i.ToString())).ToList();
        }

        private static JsonElement CreateJsonElement(XElement element)
        {
            return CreateJsonElement(element, GetElementName(element));
        }

        private static JsonElement CreateJsonElement(XElement element, string name)
        {
            switch (element.Attribute("type").Value)
            {
                case "number":
                    return new Element<string>(element.Value, name, JsonType.Number);
                case "string":
                    return new Element<string>(element.Value, name, JsonType.String);
                case "array":
                    return new Element<IList<JsonElement>>(JsonArray(element.Elements()), name, JsonType.Array);
                case "object":
                    return new Element<IDictionary<string, JsonElement>>(JsonObject(element.Elements()), name, JsonType.Object);
                case "boolean":
                    try { return new Element<bool>(bool.Parse(element.Value), name, JsonType.Bool); }
                    catch (Exception ex) { throw new ArgumentException("booleanのパースに失敗しました", ex); }
                case "null":
                    return new Element<object>(null, name, JsonType.Null);
                default:
                    throw new ArgumentException("JsonTypeの判別に失敗しました");
            }
        }

        protected JsonElement(string name, JsonType type)
        {
            this.Type = type;
            this.Name = name;
        }

        private class Element<T> : JsonElement
        {
            public T Value { get; private set; }

            public Element(T value, string name, JsonType type)
                : base(name, type)
            {
                this.Value = value;
            }

            private static string CreateEscapedString(string str)
            {
                return "\"" + str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r")
                    .Replace("\t", "\\t").Replace("\f", "\\f").Replace("\b", "\\b").Replace("/", "\\/") + "\"";
            }

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
                default: return null;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public static JsonElement Parse(string json)
        {
            return Parse(json, Encoding.UTF8);
        }

        public static JsonElement Parse(string json, Encoding encoding)
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(encoding.GetBytes(json), XmlDictionaryReaderQuotas.Max))
            {
                return JsonElement.CreateJsonElement(XElement.Load(reader));
            }
        }

        private class JsonElementDebugView
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public JsonElement[] ChildNodes { get; private set; }

            private JsonElementDebugView(JsonElement element)
            {
                this.ChildNodes = element.TypeIs(JsonType.Array | JsonType.Object) ? element.ToArray() : null;
            }
        }
    }
}
