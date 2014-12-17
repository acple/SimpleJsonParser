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
    public static class JsonParser
    {
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
    }

    [Flags]
    public enum JsonType
    { Int = 1, Double = 1 << 1, String = 1 << 2, Array = 1 << 3, Object = 1 << 4, Bool = 1 << 5, Null = 1 << 6, Undefined = 0 }

    [DebuggerDisplay("{ToString(),nq}", Name = "{Name}", Type = "JsonElement.{Type}")]
    [DebuggerTypeProxy(typeof(JsonElementDebugView))]
    public abstract class JsonElement : IEnumerable<JsonElement>
    {
        public readonly string Name;

        public readonly JsonType Type;

        public long Int { get { return GetValue<long>(JsonType.Int | JsonType.Double); } }

        public double Double { get { return GetValue<double>(JsonType.Double | JsonType.Int); } }

        public string String { get { return GetValue<string>(JsonType.String); } }

        public bool Bool { get { return GetValue<bool>(JsonType.Bool); } }

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

        public bool Exists(string key)
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
                return (this as JsonElement<T>).Value;
            throw new InvalidOperationException(string.Format("要素 \"{0}\" は {1} 型ではありません",
                this.Name, string.Join(" 型、 ", type.ToString().Split(',').Select(x => "<" + x.TrimStart() + ">"))));
        }

        private static string GetElementName(XElement element)
        {
            return element.Attribute("item") != null ? element.Attribute("item").Value : element.Name.LocalName;
        }

        private static IDictionary<string, JsonElement> JsonObject(IEnumerable<XElement> elements)
        {
            var name = string.Empty;
            return elements.ToDictionary(x => name = GetElementName(x), x => CreateJsonElement(x, name));
        }

        private static IList<JsonElement> JsonArray(IEnumerable<XElement> elements)
        {
            return elements.Select((x, i) => CreateJsonElement(x, "[" + i.ToString() + "]")).ToList();
        }

        internal static JsonElement CreateJsonElement(XElement element)
        {
            return CreateJsonElement(element, GetElementName(element));
        }

        internal static JsonElement CreateJsonElement(XElement element, string name)
        {
            switch (element.Attribute("type").Value)
            {
                case "number":
                    long l;
                    double d;
                    if (long.TryParse(element.Value, out l))
                        return new JsonElement<long>(l, name, JsonType.Int);
                    else if (double.TryParse(element.Value, out d))
                        return new JsonElement<double>(d, name, JsonType.Double);
                    else
                        throw new ArgumentException("numberのパースに失敗しました");
                case "string":
                    return new JsonElement<string>(element.Value, name, JsonType.String);
                case "array":
                    return new JsonElement<IList<JsonElement>>(JsonArray(element.Elements()), name, JsonType.Array);
                case "object":
                    return new JsonElement<IDictionary<string, JsonElement>>(JsonObject(element.Elements()), name, JsonType.Object);
                case "boolean":
                    try { return new JsonElement<bool>(bool.Parse(element.Value), name, JsonType.Bool); }
                    catch (Exception ex) { throw new ArgumentException("booleanのパースに失敗しました", ex); }
                case "null":
                    return new JsonElement<object>(null, name, JsonType.Null);
                default:
                    throw new ArgumentException("JsonTypeの判別に失敗しました");
            }
        }

        internal JsonElement(string name, JsonType type)
        {
            this.Type = type;
            this.Name = name;
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

        private class JsonElementDebugView
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public readonly JsonElement[] ChildNodes;

            private JsonElementDebugView(JsonElement element)
            {
                this.ChildNodes = element.TypeIs(JsonType.Array | JsonType.Object) ? element.ToArray() : null;
            }
        }
    }

    internal class JsonElement<T> : JsonElement
    {
        public readonly T Value;

        public JsonElement(T value, string name, JsonType type)
            : base(name, type)
        {
            this.Value = value;
        }

        public override string ToString()
        {
            switch (this.Type)
            {
                case JsonType.Int: return this.Value.ToString();
                case JsonType.Double: return this.Value.ToString();
                case JsonType.String: return CreateEscapedString(this.Value as string);
                case JsonType.Array: return "[" + string.Join(",", this.Select(x => x.ToString())) + "]";
                case JsonType.Object: return "{" + string.Join(",", this.Select(x => CreateEscapedString(x.Name) + ":" + x.ToString())) + "}";
                case JsonType.Bool: return this.Value.ToString().ToLower();
                case JsonType.Null: return "null";
                default: return null;
            }
        }

        private static string CreateEscapedString(string str)
        {
            return "\"" + str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r")
                .Replace("\t", "\\t").Replace("\f", "\\f").Replace("\b", "\\b").Replace("/", "\\/") + "\"";
        }
    }
}
