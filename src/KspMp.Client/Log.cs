using System;
using System.IO;

namespace KspMp
{
    /// <summary>
    /// All output goes to KSP.log with a [KspMp] prefix, and to GameData/KspMp/PluginData/kspmp.log as well:
    /// a friend's KSP.log is tens of megabytes and mostly not ours, and asking for it never worked. The mod's
    /// own file holds just our lines, restarts with the game, and is small enough to paste.
    /// </summary>
    internal static class Log
    {
        private const string Prefix = "[KspMp] ";
        private static readonly object Gate = new object();
        private static StreamWriter _file;

        public static string FilePath { get; private set; }

        /// <summary>Opens the mod's own log file (truncating the previous run's). Call once, from the main thread.</summary>
        public static void OpenFile(string path)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    _file = new StreamWriter(path, false) { AutoFlush = true };
                    FilePath = path;
                }
                catch (Exception e)
                {
                    _file = null;
                    UnityEngine.Debug.LogWarning(Prefix + "The mod's own log file could not be opened at " + path + ": " + e.Message);
                }
            }
        }

        public static void CloseFile()
        {
            lock (Gate)
            {
                try { _file?.Dispose(); } catch { }
                _file = null;
            }
        }

        public static void Info(string message) { UnityEngine.Debug.Log(Prefix + message); ToFile("INFO", message); }
        public static void Warn(string message) { UnityEngine.Debug.LogWarning(Prefix + message); ToFile("WARN", message); }
        public static void Error(string message) { UnityEngine.Debug.LogError(Prefix + message); ToFile("ERR ", message); }
        public static void Exception(string context, Exception e) { UnityEngine.Debug.LogError(Prefix + context + ": " + e); ToFile("EXC ", context + ": " + e); }

        // The hosting thread logs too, so the writer is shared under a lock; a file that cannot be written
        // is dropped silently rather than taking the caller down for a line of diagnostics.
        private static void ToFile(string level, string message)
        {
            if (_file == null) return;
            lock (Gate)
            {
                if (_file == null) return;
                try { _file.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + level + " " + message); }
                catch { _file = null; }
            }
        }
    }
}
