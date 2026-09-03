using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KspMp.Shared.Config
{
    /// <summary>
    /// Minimal reader/writer for KSP's ConfigNode text format (NAME { key = value  CHILD { ... } }).
    /// Lets the server read and write .cfg files (config, universe, vessel documents) without any KSP assembly.
    /// A parsed file is a root node with an empty name whose children are the file's top-level nodes.
    /// </summary>
    public sealed class CfgNode
    {
        public string Name;
        public readonly List<KeyValuePair<string, string>> Values = new List<KeyValuePair<string, string>>();
        public readonly List<CfgNode> Nodes = new List<CfgNode>();

        public CfgNode(string name = "")
        {
            Name = name ?? string.Empty;
        }

        // ---- values ----

        public string GetValue(string key)
        {
            for (var i = 0; i < Values.Count; i++)
                if (Values[i].Key == key) return Values[i].Value;
            return null;
        }

        public IEnumerable<string> GetValues(string key)
        {
            for (var i = 0; i < Values.Count; i++)
                if (Values[i].Key == key) yield return Values[i].Value;
        }

        public bool HasValue(string key) => GetValue(key) != null;

        public void AddValue(string key, string value) => Values.Add(new KeyValuePair<string, string>(key, value ?? string.Empty));
        public void AddValue(string key, int value) => AddValue(key, value.ToString(CultureInfo.InvariantCulture));
        public void AddValue(string key, long value) => AddValue(key, value.ToString(CultureInfo.InvariantCulture));
        public void AddValue(string key, float value) => AddValue(key, value.ToString("R", CultureInfo.InvariantCulture));
        public void AddValue(string key, double value) => AddValue(key, value.ToString("R", CultureInfo.InvariantCulture));
        public void AddValue(string key, bool value) => AddValue(key, value ? "True" : "False");
        public void AddValue(string key, Guid value) => AddValue(key, value.ToString());

        /// <summary>Replaces the first value with this key, or adds it.</summary>
        public void SetValue(string key, string value)
        {
            for (var i = 0; i < Values.Count; i++)
            {
                if (Values[i].Key != key) continue;
                Values[i] = new KeyValuePair<string, string>(key, value ?? string.Empty);
                return;
            }
            AddValue(key, value);
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            var v = GetValue(key);
            return v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : defaultValue;
        }

        public long GetLong(string key, long defaultValue = 0)
        {
            var v = GetValue(key);
            return v != null && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : defaultValue;
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            var v = GetValue(key);
            return v != null && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : defaultValue;
        }

        public double GetDouble(string key, double defaultValue = 0d)
        {
            var v = GetValue(key);
            return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : defaultValue;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            var v = GetValue(key);
            if (v == null) return defaultValue;
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return defaultValue;
        }

        public Guid GetGuid(string key, Guid defaultValue = default(Guid))
        {
            var v = GetValue(key);
            return v != null && Guid.TryParse(v, out var r) ? r : defaultValue;
        }

        // ---- nodes ----

        public CfgNode GetNode(string name)
        {
            for (var i = 0; i < Nodes.Count; i++)
                if (Nodes[i].Name == name) return Nodes[i];
            return null;
        }

        public IEnumerable<CfgNode> GetNodes(string name)
        {
            for (var i = 0; i < Nodes.Count; i++)
                if (Nodes[i].Name == name) yield return Nodes[i];
        }

        public CfgNode AddNode(string name)
        {
            var node = new CfgNode(name);
            Nodes.Add(node);
            return node;
        }

        // ---- text ----

        public static CfgNode Parse(string text)
        {
            var root = new CfgNode(string.Empty);
            var stack = new Stack<CfgNode>();
            stack.Push(root);
            string pendingName = null;

            foreach (var line in Tokenize(text))
            {

                if (line == "{")
                {
                    Push(stack, pendingName ?? string.Empty);
                    pendingName = null;
                    continue;
                }
                if (line == "}")
                {
                    if (stack.Count > 1) stack.Pop();
                    pendingName = null;
                    continue;
                }
                if (line.EndsWith("{", StringComparison.Ordinal))
                {
                    Push(stack, line.Substring(0, line.Length - 1).Trim());
                    pendingName = null;
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq >= 0)
                {
                    pendingName = null;
                    stack.Peek().Values.Add(new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
                    continue;
                }

                pendingName = line; // a node name; its '{' follows on the next line
            }
            return root;
        }

        /// <summary>Strips comments and, like KSP's PreFormatConfig, puts every '{' and '}' on its own logical line.</summary>
        private static IEnumerable<string> Tokenize(string text)
        {
            foreach (var rawLine in (text ?? string.Empty).Split('\n'))
            {
                var line = rawLine;
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) line = line.Substring(0, comment);
                var start = 0;
                for (var i = 0; i < line.Length; i++)
                {
                    if (line[i] != '{' && line[i] != '}') continue;
                    var before = line.Substring(start, i - start).Trim();
                    if (before.Length > 0) yield return before;
                    yield return line[i] == '{' ? "{" : "}";
                    start = i + 1;
                }
                var rest = line.Substring(start).Trim();
                if (rest.Length > 0) yield return rest;
            }
        }

        private static void Push(Stack<CfgNode> stack, string name)
        {
            var node = new CfgNode(name);
            stack.Peek().Nodes.Add(node);
            stack.Push(node);
        }

        public static CfgNode Load(string path) => Parse(File.ReadAllText(path));

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, ToText());
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            if (string.IsNullOrEmpty(Name)) WriteChildren(sb, 0);
            else Write(sb, 0);
            return sb.ToString();
        }

        private void Write(StringBuilder sb, int indent)
        {
            Indent(sb, indent).Append(Name).Append('\n');
            Indent(sb, indent).Append("{\n");
            WriteChildren(sb, indent + 1);
            Indent(sb, indent).Append("}\n");
        }

        private void WriteChildren(StringBuilder sb, int indent)
        {
            for (var i = 0; i < Values.Count; i++)
                Indent(sb, indent).Append(Values[i].Key).Append(" = ").Append(Values[i].Value).Append('\n');
            for (var i = 0; i < Nodes.Count; i++)
                Nodes[i].Write(sb, indent);
        }

        private static StringBuilder Indent(StringBuilder sb, int indent) => sb.Append('\t', indent);

        public override string ToString() => (Name.Length == 0 ? "(root)" : Name) + " [" + Values.Count + " values, " + Nodes.Count + " nodes]";
    }
}
