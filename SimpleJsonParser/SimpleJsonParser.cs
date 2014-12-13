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
                return new JsonElement(XElement.Load(reader));
            }
        }
    }

    [Flags]
    public enum JsonType : byte
    { Int = 1, Double = 1 << 1, String = 1 << 2, Array = 1 << 3, Object = 1 << 4, Bool = 1 << 5, Null = 1 << 6, Undefined = 0 }

    [DebuggerDisplay("{ToString(),nq}", Name = "{Name}", Type = "JsonElement.{Type}")]
    public class JsonElement : IEnumerable<JsonElement>
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private object Value; //long, double, string, List<JsonElement>, Dictionary<string, JsonElement>, bool, null

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public string Name { get; private set; }

        public JsonType Type { get; private set; }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public long Int { get { return GetValue<long>(JsonType.Int | JsonType.Double); } }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public double Double { get { return GetValue<double>(JsonType.Double | JsonType.Int); } }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public string String { get { return GetValueAs<string>(JsonType.String); } }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public bool Bool { get { return GetValue<bool>(JsonType.Bool); } }

        public JsonElement this[string key]
        {
            get
            {
                try { return GetValueAs<IDictionary<string, JsonElement>>(JsonType.Object)[key]; }
                catch (KeyNotFoundException ex)
                { throw new KeyNotFoundException(string.Format("要素名 \"{0}\" が [{1}] に存在しません", key, this.Name), ex); }
            }
        }

        public JsonElement this[int index]
        {
            get { return GetValueAs<IList<JsonElement>>(JsonType.Array)[index]; }
        }

        public bool Exists(string name)
        {
            return GetValueAs<IDictionary<string, JsonElement>>(JsonType.Object).ContainsKey(name);
        }

        public int Count()
        {
            return GetValueAs<System.Collections.ICollection>(JsonType.Array | JsonType.Object).Count;
        }

        public bool TypeIs(JsonType flag)
        {
            return 0 != (this.Type & flag);
        }

        private T GetValue<T>(JsonType type) where T : struct
        {
            if (TypeIs(type))
                return (T)this.Value;
            throw new InvalidOperationException(string.Format("要素 \"{0}\" は {1} 型ではありません",
                this.Name, string.Join(" 型、 ", type.ToString().Split(',').Select(x => "<" + x.TrimStart() + ">"))));
        }

        private T GetValueAs<T>(JsonType type) where T : class
        {
            if (TypeIs(type))
                return this.Value as T;
            throw new InvalidOperationException(string.Format("要素 \"{0}\" は {1} 型ではありません",
                this.Name, string.Join(" 型、 ", type.ToString().Split(',').Select(x => "<" + x.TrimStart() + ">"))));
        }

        private static IDictionary<string, JsonElement> JsonObject(IEnumerable<XElement> elements)
        {
            return elements.ToDictionary(x => GetElementName(x), x => new JsonElement(x));
        }

        private static IList<JsonElement> JsonArray(IEnumerable<XElement> elements)
        {
            return elements.Select((x, i) => new JsonElement(x, i)).ToList();
        }

        private static string GetElementName(XElement element)
        {
            return element.Attribute("item") != null ? element.Attribute("item").Value : element.Name.LocalName;
        }

        private void Set<T>(T value, string name, JsonType type)
        {
            this.Value = value;
            this.Name = name;
            this.Type = type;
        }

        internal JsonElement(XElement element)
        {
            switch (element.Attribute("type").Value)
            {
                case "number":
                    long l;
                    double d;
                    if (long.TryParse(element.Value, out l))
                        Set(l, GetElementName(element), JsonType.Int);
                    else if (double.TryParse(element.Value, out d))
                        Set(d, GetElementName(element), JsonType.Double);
                    else
                        throw new ArgumentException("numberのパースに失敗しました");
                    break;
                case "string":
                    Set(element.Value, GetElementName(element), JsonType.String);
                    break;
                case "array":
                    Set(JsonArray(element.Elements()), GetElementName(element), JsonType.Array);
                    break;
                case "object":
                    Set(JsonObject(element.Elements()), GetElementName(element), JsonType.Object);
                    break;
                case "boolean":
                    try { Set(bool.Parse(element.Value), GetElementName(element), JsonType.Bool); }
                    catch (Exception ex) { throw new ArgumentException("booleanのパースに失敗しました", ex); }
                    break;
                case "null":
                    Set<object>(null, GetElementName(element), JsonType.Null);
                    break;
                default:
                    throw new ArgumentException("JsonTypeの判別に失敗しました");
            }
        }

        internal JsonElement(XElement element, int index)
            : this(element)
        {
            this.Name += "[" + index.ToString() + "]";
        }

        public override string ToString()
        {
            switch (this.Type)
            {
                case JsonType.Int: return ((long)this.Value).ToString();
                case JsonType.Double: return ((double)this.Value).ToString();
                case JsonType.String: return "\"" + ReplaceEscapeChars(this.Value as string) + "\"";
                case JsonType.Array: return "[" + string.Join(",", this.Select(x => x.ToString())) + "]";
                case JsonType.Object: return "{" + string.Join(",", this.Select(x => "\"" + ReplaceEscapeChars(x.Name) + "\":" + x.ToString())) + "}";
                case JsonType.Bool: return ((bool)this.Value).ToString().ToLower();
                case JsonType.Null: return "null";
                default: return null;
            }
        }

        private static string ReplaceEscapeChars(string str)
        {
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r")
                .Replace("\t", "\\t").Replace("\f", "\\f").Replace("\b", "\\b").Replace("/", "\\/");
        }

        public IEnumerator<JsonElement> GetEnumerator()
        {
            if (this.Type == JsonType.Object)
            {
                foreach (var element in this.Value as IDictionary<string, JsonElement>)
                    yield return element.Value;
            }
            else if (this.Type == JsonType.Array)
            {
                foreach (var element in this.Value as IList<JsonElement>)
                    yield return element;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
