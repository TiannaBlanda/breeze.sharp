
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Breeze.Sharp.Core;
using Newtonsoft.Json.Serialization;
using System.Globalization;
using System.Reflection;

namespace Breeze.Sharp {

  /// <summary>
  /// For internal use only.
  /// </summary>
  public interface IJsonSerializable {
    JNode ToJNode(Object config);
  }

  /// <summary>
  /// For internal use only. Wrapper over simple json object to provide serialization/deserialization services.
  /// </summary>
  public class JNode {
    public JNode() {
      _jo = new JObject();
    }

    public JNode(JObject jo) {
      _jo = jo;
    }

    public static JNode FromObject(Object o, bool shouldCamelCase) {
      JObject jo;
      if (shouldCamelCase) {
        jo = (JObject)JToken.FromObject(o, CamelCaseSerializer);
      } else {
        jo = (JObject)JToken.FromObject(o);
      }
      return new JNode(jo);
    }

    public bool IsEmpty {
      get {
        return !_jo.Values().Any();
      }
    }

    public Object ToObject(Type t, bool shouldCamelCase = false) {
      if (shouldCamelCase) {
        return _jo.ToObject(t, CamelCaseSerializer);
      }
      return _jo.ToObject(t);
    }

    public override String ToString() {
      return _jo.ToString();
    }

    public Object Config {
      get;
      set;
    }

    public bool HasValues(String propName) {
      var jt = _jo[propName];
      return jt != null && jt.HasValues;
    }

    #region Add Methods

    public void AddPrimitive(String propName, Object value, Object defaultValue = null) {
      if (value == null) return;
      if (value != null && value.Equals(defaultValue)) return;
      AddRaw(propName, new JValue(value));
    }

    public void AddEnum<TEnum>(String propName, TEnum value) where TEnum : struct {
      AddRaw(propName, new JValue(value.ToString()));
    }

    public void AddEnum<TEnum>(String propName, TEnum? value) where TEnum : struct {
      if (value == null) return;
      AddRaw(propName, new JValue(value.ToString()));
    }

    public void AddJNode(String propName, IJsonSerializable item) {
      if (item == null) return;
      var jn = item.ToJNode(null);
      AddRaw(propName, jn._jo);
    }

    public void AddArray<T>(String propName, IEnumerable<T> items) {
      if (!items.Any()) return;
      var ja = ToJArray(items);
      AddRaw(propName, ja);
    }

    public void AddArray<T>(String propName, IEnumerable<T> items, Func<T, JNode> func) {
      if (!items.Any()) return;
      var ja = ToJArray(items, func);
      AddRaw(propName, ja);
    }

    public void AddMap<T>(String propName, IDictionary<String, T> map) {
      if (map == null) return;
      if (!map.Values.Any()) return;

      var jn = BuildMapNode<T>(map);

      AddRaw(propName, jn._jo);
    }


    public void AddJNode(String propName, JNode jn) {
      if (jn == null) return;
      if (jn.IsEmpty) return;
      AddRaw(propName, jn._jo);
    }

    private void AddRaw(String propName, JToken jt) {
      _jo.Add(propName, jt);
    }

    #endregion

    #region Get methods 


    public Object Get(String propName, Type objectType) {
      var prop = _jo.Property(propName);
      if (prop == null) return null;
      var nonnullableType = objectType.GenericTypeArguments.FirstOrDefault();
      // TODO: Ugh.. hack to allow latest NewtonSoft Json to work correctly with nullable enums. 
      Object val;
      if (nonnullableType != null && nonnullableType.GetTypeInfo().IsEnum) {
        val = Enum.Parse(nonnullableType, prop.Value.ToString());
      } else {
        val = prop.Value.ToObject(objectType);
      }
      return val;
    }

    public T Get<T>(String propName, T defaultValue = default(T)) {
      var prop = _jo.Property(propName);
      if (prop == null) return defaultValue;
      var val = prop.Value.ToObject<T>();
      return val;
    }

    public T GetToken<T>(String propName) where T : JToken {
      var prop = _jo.Property(propName);
      if (prop == null) return null;
      return (T)prop.Value;

    }

    public TEnum GetEnum<TEnum>(String propName, TEnum defaultValue = default(TEnum)) {
      var val = Get<String>(propName);
      if (val == null) {
        return defaultValue;
      } else {
        return (TEnum)Enum.Parse(typeof(TEnum), val);
      }
    }

    public TEnum? GetNullableEnum<TEnum>(String propName) where TEnum : struct {
      var val = Get<String>(propName);
      if (val == null) {
        return null;
      } else {
        return (TEnum)Enum.Parse(typeof(TEnum), val);
      }
    }

    // for non newable types like String, Int etc..
    public IEnumerable<T> GetArray<T>(String propName) {
      var items = GetToken<JArray>(propName);
      if (items == null) {
        return Enumerable.Empty<T>();
      } else {
        return items.Select(item => {
          return item.ToObject<T>();
        });
      }
    }

    public IEnumerable<T> GetArray<T>(params String[] propNames) {
      var items = propNames.Select(pn => GetToken<JArray>(pn)).FirstOrDefault(jt => jt != null);
      if (items == null) {
        return Enumerable.Empty<T>();
      } else {
        return items.Select(item => {
          return item.ToObject<T>();
        });
      }
    }

    public IEnumerable<Object> GetArray(String propName, IEnumerable<Type> toTypes) {
      var items = GetToken<JArray>(propName);
      if (items == null) {
        return Enumerable.Empty<Object>();
      } else {
        return items.Zip(toTypes, (item, type) => {
          return item.ToObject(type);
        });
      }
    }

    public Dictionary<String, T> GetMap<T>(String propName) {
      var map = (JObject)GetToken<JObject>(propName);

      var rmap = new Dictionary<String, T>();
      if (map == null) return rmap;
      foreach (var kvp in map) {
        rmap.Add(kvp.Key, kvp.Value.ToObject<T>());
      }
      return rmap;
    }

    public Dictionary<String, Object> GetMap(String propName, Func<String, Type> toTypeFn) {
      var map = (JObject)GetToken<JObject>(propName);
      if (map == null) return null;
      var rmap = new Dictionary<String, Object>();
      foreach (var kvp in map) {
        var toType = toTypeFn(kvp.Key);
        rmap.Add(kvp.Key, kvp.Value.ToObject(toType));
      }
      return rmap;
    }

    public JNode GetJNode(String propName) {
      var item = (JObject)GetToken<JObject>(propName);
      if (item == null) return null;
      var jNode = new JNode(item);
      return jNode;
    }

    //public T GetObject<T>(String propName, Func<JNode, T> ctorFn) {
    //  var item = (JObject)GetToken<JObject>(propName);
    //  if (item == null) return default(T);
    //  var jNode = new JNode(item);
    //  return ctorFn(jNode);
    //}

    public IEnumerable<JNode> GetJNodeArray(String propName) {
      var items = GetToken<JArray>(propName);
      if (items == null) {
        return Enumerable.Empty<JNode>();
      } else {
        return items.Select(item => new JNode((JObject)item));
      }
    }

    public IDictionary<String, JNode> GetJNodeMap(String propName) {
      var map = GetToken<JObject>(propName);
      if (map == null) return null;
      var rmap = ((IDictionary<String, JToken>)map).ToDictionary(kvp => kvp.Key, kvp => new JNode((JObject)kvp.Value));
      return rmap;
    }

    public IDictionary<String, IEnumerable<JNode>> GetJNodeArrayMap(String propName) {
      var map = GetToken<JObject>(propName);
      if (map == null) return null;
      var rmap = new Dictionary<String, IEnumerable<JNode>>();
      foreach (var kvp in map) {
        var ja = (JArray)kvp.Value;
        var values = ja.Select(item => new JNode((JObject)item));
        rmap.Add(kvp.Key, values);
      }
      return rmap;
    }



    // pass in a simple value, a JNode or a IJsonSerializable and returns either a simple value or a JObject or a JArray
    private static Object CvtValue(Object value) {
      var jn = value as JNode;
      if (jn != null) {
        return jn._jo;
      }

      var nodes = value as IEnumerable<JNode>;
      if (nodes != null) {
        return ToJArray(nodes);
      }

      var js = value as IJsonSerializable;
      if (js != null) {
        return js.ToJNode(null)._jo;
      }

      return value;
    }

    #endregion

    #region Serialize/Deserialize fns

    public String Serialize() {
      using (var stringWriter = new StringWriter()) {
        return SerializeTo(stringWriter).ToString();
      }
    }

    public Stream SerializeTo(Stream stream) {
      using (var streamWriter = new StreamWriter(stream)) {
        SerializeTo(streamWriter);
      }
      stream.Position = 0;
      return stream;
    }

    public TextWriter SerializeTo(TextWriter textWriter) {
      var serializer = new JsonSerializer() { MaxDepth = 128 };
      serializer.Converters.Add(new StringEnumConverter());

#if DEBUG
      serializer.Formatting = Formatting.Indented;
#else
      serializer.Formatting = Formatting.None;
#endif

      using (var jtw = new JsonTextWriter(textWriter)) {
        serializer.Serialize(jtw, _jo);
        jtw.Flush();
      }
      return textWriter;
    }

    public static JNode DeserializeFrom(Stream stream) {
      return DeserializeFrom(new StreamReader(stream));
    }

    public static JNode DeserializeFrom(string json) {
      return DeserializeFrom(new StringReader(json));
    }

    public static JNode DeserializeFrom(TextReader textReader) {
      var reader = new JsonTextReader(textReader) { MaxDepth = 128 };
      // needed because we need to set the DateParseHandling to work with DataTimeOffsets
      reader.DateParseHandling = DateParseHandling.DateTimeOffset;
      var jo = JObject.Load(reader);
      //if (reader.Read() && reader.TokenType != JsonToken.Comment) {
      //  JObject.Parse(json);
      //}
      return new JNode(jo);
    }

    #endregion

    #region Other methods

    public static JNode BuildMapNode<T>(IDictionary<String, T> map) {
      var jn = new JNode();
      map.ForEach(kvp => {
        var val = CvtValue(kvp.Value);
        if (val != null) {
          if (val is JToken) {
            jn.AddRaw(kvp.Key, (JToken)val);
          } else {
            jn.AddRaw(kvp.Key, new JValue(val));
          }
        } else {
          jn.AddRaw(kvp.Key, null);
        }
      });
      return jn;
    }

    public static JArray ToJArray<T>(IEnumerable<T> items) {
      var ja = new JArray();
      items.ForEach(v => ja.Add(CvtValue(v)));
      return ja;
    }

    public static JArray ToJArray<T>(IEnumerable<T> items, Func<T, JNode> func) {
      var ja = new JArray();
      items.ForEach(v => ja.Add(func(v)));
      return ja;
    }

    public override bool Equals(object obj) {
      if (obj == this) return true;
      var other = obj as JNode;
      if (other == null) return false;
      return EqualityComparer.Equals(this._jo, other._jo);
    }

    public override int GetHashCode() {
      return EqualityComparer.GetHashCode(this._jo);
    }

    #endregion

    internal JObject _jo;

    private static JsonSerializer CamelCaseSerializer = CreateCamelCaseSerializer();

    private static JsonSerializer CreateCamelCaseSerializer() {
      var s = new JsonSerializer() {
        MaxDepth = 128,
        ContractResolver = new CamelCasePropertyNamesContractResolver()
      };
      s.Converters.Add(new DictionaryKeysAreNotPropertyNamesJsonConverter());
      return s;
    }

    private static JTokenEqualityComparer EqualityComparer = new JTokenEqualityComparer();
  }

  public class DictionaryKeysAreNotPropertyNamesJsonConverter : JsonConverter {
    public override bool CanConvert(Type objectType) {
      return typeof(IDictionary).IsAssignableFrom(objectType);
    }

    public override bool CanRead {
      get { return false; }
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
      throw new InvalidOperationException();
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
      IDictionary dictionary = (IDictionary)value;

      writer.WriteStartObject();

      foreach (DictionaryEntry entry in dictionary) {
        string key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
        writer.WritePropertyName(key);
        serializer.Serialize(writer, entry.Value);
      }

      writer.WriteEndObject();
    }
  }

}

