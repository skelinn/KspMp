using System;

namespace KspMp
{
    /// <summary>All output goes to KSP.log with a [KspMp] prefix.</summary>
    internal static class Log
    {
        private const string Prefix = "[KspMp] ";

        public static void Info(string message) => UnityEngine.Debug.Log(Prefix + message);
        public static void Warn(string message) => UnityEngine.Debug.LogWarning(Prefix + message);
        public static void Error(string message) => UnityEngine.Debug.LogError(Prefix + message);
        public static void Exception(string context, Exception e) => UnityEngine.Debug.LogError(Prefix + context + ": " + e);
    }
}
